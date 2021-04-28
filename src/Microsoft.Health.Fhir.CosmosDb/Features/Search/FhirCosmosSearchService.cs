﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class FhirCosmosSearchService : SearchService
    {
        private static readonly SearchParameterInfo _wildcardReferenceSearchParameter = new(SearchValueConstants.WildcardReferenceSearchParameterName, SearchValueConstants.WildcardReferenceSearchParameterName);

        private readonly CosmosFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IFhirRequestContextAccessor _requestContextAccessor;
        private readonly CosmosDataStoreConfiguration _cosmosConfig;
        private readonly ICosmosDbCollectionPhysicalPartitionInfo _physicalPartitionInfo;
        private readonly QueryPartitionStatisticsCache _queryPartitionStatisticsCache;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly SearchParameterInfo _resourceIdSearchParameter;
        private const int _chainedSearchMaxSubqueryItemLimit = 100;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IFhirRequestContextAccessor requestContextAccessor,
            CosmosDataStoreConfiguration cosmosConfig,
            ICosmosDbCollectionPhysicalPartitionInfo physicalPartitionInfo,
            QueryPartitionStatisticsCache queryPartitionStatisticsCache)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(cosmosConfig, nameof(cosmosConfig));
            EnsureArg.IsNotNull(physicalPartitionInfo, nameof(physicalPartitionInfo));
            EnsureArg.IsNotNull(queryPartitionStatisticsCache, nameof(queryPartitionStatisticsCache));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _requestContextAccessor = requestContextAccessor;
            _cosmosConfig = cosmosConfig;
            _physicalPartitionInfo = physicalPartitionInfo;
            _queryPartitionStatisticsCache = queryPartitionStatisticsCache;
            _resourceTypeSearchParameter = searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
            _resourceIdSearchParameter = searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.Id);
        }

        protected override async Task<SearchResult> SearchInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            // we're going to mutate searchOptions, so clone it first so the caller of this method does not see the changes.
            searchOptions = searchOptions.Clone();

            // rewrite DateTime range expressions to be more efficient
            searchOptions.Expression = searchOptions.Expression?.AcceptVisitor(DateTimeEqualityRewriter.Instance);

            // pull out the _include and _revinclude expressions.
            bool hasIncludeOrRevIncludeExpressions = ExtractIncludeAndChainedExpressions(
                searchOptions.Expression,
                out Expression expressionWithoutIncludes,
                out IReadOnlyList<IncludeExpression> includeExpressions,
                out IReadOnlyList<IncludeExpression> revIncludeExpressions,
                out IReadOnlyList<ChainedExpression> chainedExpressions);

            if (hasIncludeOrRevIncludeExpressions)
            {
                searchOptions.Expression = expressionWithoutIncludes;

                if (includeExpressions.Any(e => e.Iterate) || revIncludeExpressions.Any(e => e.Iterate))
                {
                    // We haven't implemented this yet.
                    throw new BadRequestException(Resources.IncludeIterateNotSupported);
                }
            }

            if (hasIncludeOrRevIncludeExpressions && chainedExpressions.Count > 0)
            {
                List<Expression> chainedReferences = await PerformChainedSearch(searchOptions, chainedExpressions, cancellationToken);

                // Expressions where provided and some results to filter by were found.
                if (chainedReferences?.Count == chainedExpressions.Count)
                {
                    chainedReferences.Insert(0, searchOptions.Expression);
                    searchOptions.Expression = Expression.And(chainedReferences);
                }
                else
                {
                    if (searchOptions.CountOnly)
                    {
                        return new SearchResult(0, searchOptions.UnsupportedSearchParams);
                    }

                    return CreateSearchResult(searchOptions, ArraySegment<SearchResultEntry>.Empty, continuationToken: null);
                }
            }

            if (searchOptions.CountOnly)
            {
                int count = await ExecuteCountSearchAsync(
                    _queryBuilder.BuildSqlQuerySpec(searchOptions),
                    cancellationToken);

                return new SearchResult(count, searchOptions.UnsupportedSearchParams);
            }

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, _) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.BuildSqlQuerySpec(searchOptions, new QueryBuilderOptions(includeExpressions)),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                null,
                null,
                cancellationToken);

            (IList<FhirCosmosResourceWrapper> includes, bool includesTruncated) = await PerformIncludeQueries(results, includeExpressions, revIncludeExpressions, searchOptions.IncludeCount, cancellationToken);

            SearchResult searchResult = CreateSearchResult(
                searchOptions,
                results.Select(m => new SearchResultEntry(m, SearchEntryMode.Match)).Concat(includes.Select(i => new SearchResultEntry(i, SearchEntryMode.Include))),
                continuationToken,
                includesTruncated);

            if (searchOptions.IncludeTotal == TotalType.Accurate)
            {
                Debug.Assert(!searchOptions.CountOnly, "We should not be computing the total of a CountOnly search.");

                searchOptions = searchOptions.Clone();

                // If this is the first page and there aren't any more pages
                if (searchOptions.ContinuationToken == null && continuationToken == null)
                {
                    // Count the results on the page.
                    searchResult.TotalCount = results.Count;
                }
                else
                {
                    // Otherwise, indicate that we'd like to get the count
                    searchOptions.CountOnly = true;

                    // And perform a second read.
                    searchResult.TotalCount = await ExecuteCountSearchAsync(
                        _queryBuilder.BuildSqlQuerySpec(searchOptions),
                        cancellationToken);
                }
            }

            return searchResult;
        }

        private async Task<List<Expression>> PerformChainedSearch(SearchOptions searchOptions, IReadOnlyList<ChainedExpression> chainedExpressions, CancellationToken cancellationToken)
        {
            if (!_cosmosConfig.EnableChainedSearch &&
                !(_requestContextAccessor.FhirRequestContext.RequestHeaders.TryGetValue(KnownHeaders.EnableChainSearch, out StringValues featureSetting) &&
                  string.Equals(featureSetting.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase)))
            {
                throw new SearchOperationNotSupportedException(Resources.ChainedExpressionNotSupported);
            }

            var chainedReferences = new List<Expression>();
            SearchOptions chainedOptions = searchOptions.Clone();
            chainedOptions.CountOnly = false;
            chainedOptions.MaxItemCount = _chainedSearchMaxSubqueryItemLimit;
            chainedOptions.MaxItemCountSpecifiedByClient = false;

            // Loop over all chained expressions in the search query and pass them into the local recursion function
            foreach (ChainedExpression item in chainedExpressions)
            {
                Expression recursiveLookup = await RecurseChainedExpression(item, chainedOptions, cancellationToken);

                if (recursiveLookup == null)
                {
                    return null;
                }

                chainedReferences.Add(recursiveLookup);
            }

            return chainedReferences;
        }

        /// <summary>
        /// This lets us walk to the end of the expression to start filtering, the results are used to filter against the parent layer.
        /// </summary>
        private async Task<Expression> RecurseChainedExpression(ChainedExpression expression, SearchOptions chainedOptions, CancellationToken cancellationToken)
        {
            Expression criteria = expression.Expression;

            if (expression.Expression is ChainedExpression innerChained)
            {
                criteria = await RecurseChainedExpression(innerChained, chainedOptions, cancellationToken);
            }

            if (criteria == null)
            {
                // No items where returned by the sub-queries, break
                return null;
            }

            string filteredType = expression.TargetResourceTypes.First();
            var includeExpressions = new List<IncludeExpression>();

            if (expression.Reversed)
            {
                // When reversed we'll use the Include expression code to return the ids
                // in the search index on the matched resources
                foreach (var targetInclude in expression.TargetResourceTypes)
                {
                    includeExpressions.Add(Expression.Include(
                        expression.ResourceTypes,
                        expression.ReferenceSearchParameter,
                        null,
                        targetInclude,
                        expression.TargetResourceTypes,
                        false,
                        false,
                        false));
                }

                // When reversed the ids from the sub-query will match the base resource type
                filteredType = expression.ResourceTypes.First();
            }

            MultiaryExpression filterExpression = Expression.And(
                Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, filteredType, false)),
                criteria);

            chainedOptions.Expression = filterExpression;

            var chainedResults = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.BuildSqlQuerySpec(chainedOptions, new QueryBuilderOptions(includeExpressions, projection: includeExpressions.Any() ? QueryProjection.ReferencesOnly : QueryProjection.Id)),
                chainedOptions,
                null,
                null,
                null,
                cancellationToken);

            if (!string.IsNullOrEmpty(chainedResults.continuationToken))
            {
                throw new InvalidSearchOperationException(string.Format(Resources.ChainedExpressionSubqueryLimit, _chainedSearchMaxSubqueryItemLimit));
            }

            Expression[] chainedExpressionReferences;

            if (!expression.Reversed)
            {
                // When normal chained expression we can filter using references in the parent object. e.g. Observation.subject
                // The following expression constrains "subject" references on "Observation" with the ids that have matched the sub-query
                chainedExpressionReferences = chainedResults.results.Select(x =>
                        Expression.SearchParameter(
                            expression.ReferenceSearchParameter,
                            Expression.And(
                                Expression.Equals(FieldName.ReferenceResourceId, null, x.Id),
                                Expression.Equals(FieldName.ReferenceResourceType, null, filteredType))))
                    .ToArray<Expression>();
            }
            else
            {
                // When reverse chained, we take the ids and types from the child object and use it to filter the parent objects.
                // e.g. Patient?_has:Group:member:_id=group1. In this case we would have run the query there Group.id = group1
                // and returned the indexed entries for Group.member. The following query will use these items to filter the parent Patient query.
                chainedExpressionReferences = chainedResults.results.SelectMany(x =>
                        x.ReferencesToInclude.Select(include =>
                            Expression.And(
                                Expression.SearchParameter(
                                    _resourceIdSearchParameter,
                                    Expression.Equals(FieldName.TokenCode, null, include.ResourceId)),
                                Expression.SearchParameter(
                                    _resourceTypeSearchParameter,
                                    Expression.Equals(FieldName.TokenCode, null, include.ResourceTypeName)))))
                    .ToArray<Expression>();
            }

            return chainedExpressionReferences.Length > 1 ? Expression.Or(chainedExpressionReferences) : chainedExpressionReferences.FirstOrDefault();
        }

        protected override async Task<SearchResult> SearchHistoryInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, _) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.GenerateHistorySql(searchOptions),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                null,
                null,
                cancellationToken);

            return CreateSearchResult(searchOptions, results.Select(r => new SearchResultEntry(r)), continuationToken);
        }

        protected override async Task<SearchResult> SearchForReindexInternalAsync(
            SearchOptions searchOptions,
            string searchParameterHash,
            CancellationToken cancellationToken)
        {
            QueryDefinition queryDefinition = _queryBuilder.GenerateReindexSql(searchOptions, searchParameterHash);

            if (searchOptions.CountOnly)
            {
                int count = await ExecuteCountSearchAsync(queryDefinition, cancellationToken);
                return new SearchResult(count, searchOptions.UnsupportedSearchParams);
            }

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, _) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                queryDefinition,
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                null,
                null,
                cancellationToken);

            return CreateSearchResult(searchOptions, results.Select(r => new SearchResultEntry(r)), continuationToken);
        }

        /// <summary>
        /// Executes a search query. Determines whether to parallelize the query across physical partitions based on previous performance of similar queries, unless <paramref name="queryRequestOptionsOverride"/> is specified.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="sqlQuerySpec">The query to execute</param>
        /// <param name="searchOptions">The <see cref="SearchOptions"/> for this query.</param>
        /// <param name="continuationToken">The continuation token or null.</param>
        /// <param name="searchEnumerationTimeoutOverrideIfSequential">If method determines to execute the query sequentially across partitions, this optional value overrides the maximum amount of time to wait to attempt to obtain results.</param>
        /// <param name="queryRequestOptionsOverride">Specifies the <see cref="QueryRequestOptions"/> instead of this method determining them. Optional.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A tuple with the results, a possible continuation token, and the maximum degree of parallelism that was applied to the query. The latter corresponds to <see cref="QueryRequestOptions.MaxConcurrency"/></returns>
        private async Task<(IReadOnlyList<T> results, string continuationToken, int? maxConcurrency)> ExecuteSearchAsync<T>(
            QueryDefinition sqlQuerySpec,
            SearchOptions searchOptions,
            string continuationToken,
            TimeSpan? searchEnumerationTimeoutOverrideIfSequential,
            QueryRequestOptions queryRequestOptionsOverride,
            CancellationToken cancellationToken)
        {
            var feedOptions = queryRequestOptionsOverride ??
                              new QueryRequestOptions
                              {
                                  MaxItemCount = searchOptions.MaxItemCount,
                              };

            // If the database has many physical physical partitions, and this query is selective, we will want to instruct
            // the Cosmos DB SDK to query the partitions in parallel. If the query is not selective, we want to stick to
            // sequential querying, so that we do not waste RUs and time on results that will be discarded.
            // It's hard for us to determine the selectivity of an arbitrary search, so we maintain a cache of queries, keyed
            // by their value-insensitive search expression, where we accumulate averages of the number of physical partitions
            // a query hits. That way, when we see a similar query again, we can have a better idea of whether it will be selective or not.
            // This does not work perfectly. A query like Observation?date=2013-08-23T22:44:23 is likely to be selective, but
            // Observation?date=2013 is not, and these two searches look the same, but with different values.
            // Additionally, when we query sequentially, we would like to gradually fan out the parallelism, but the Cosmos DB SDK
            // does not currently properly support that. See https://github.com/Azure/azure-cosmos-dotnet-v3/issues/2290

            QueryPartitionStatistics queryPartitionStatistics = null;
            IFhirRequestContext fhirRequestContext = null;
            ConcurrentBag<ResponseMessage> messagesList = null;
            if (_physicalPartitionInfo.PhysicalPartitionCount > 1 && queryRequestOptionsOverride == null)
            {
                if (searchOptions.Sort?.Count > 0)
                {
                    feedOptions.MaxConcurrency = CosmosFhirDataStore.MaxQueryConcurrency;
                }
                else
                {
                    if (searchOptions.Expression != null && // without a filter the query will not be selective
                        string.IsNullOrEmpty(searchOptions.ContinuationToken))
                    {
                        // Telemetry currently shows that when there is a continuation token, the the query only hits one partition.
                        // This may not be true forever, in which case we would want to encode the max concurrency in the continuation token.

                        queryPartitionStatistics = _queryPartitionStatisticsCache.GetQueryPartitionStatistics(searchOptions.Expression);
                        if (IsQuerySelective(queryPartitionStatistics))
                        {
                            feedOptions.MaxConcurrency = CosmosFhirDataStore.MaxQueryConcurrency;
                        }

                        // plant a ConcurrentBag int the request context's properties, so the CosmosResponseProcessor
                        // knows to add the individual ResponseMessages sent as part of this search.

                        fhirRequestContext = _requestContextAccessor.FhirRequestContext;
                        if (fhirRequestContext != null)
                        {
                            messagesList = new ConcurrentBag<ResponseMessage>();
                            fhirRequestContext.Properties[Constants.CosmosDbResponseMessagesProperty] = messagesList;
                        }
                    }
                }
            }

            try
            {
                (IReadOnlyList<T> results, string nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, continuationToken, searchOptions.MaxItemCountSpecifiedByClient, feedOptions.MaxConcurrency == null ? searchEnumerationTimeoutOverrideIfSequential : null, cancellationToken);

                if (queryPartitionStatistics != null && messagesList != null)
                {
                    if (results.Count < feedOptions.MaxItemCount * CosmosFhirDataStore.ExecuteDocumentQueryAsyncMinimumFillFactor && string.IsNullOrEmpty(continuationToken))
                    {
                        // ExecuteDocumentQueryAsync gave up on filling the pages. This suggests that we would have been better off querying in parallel.
                        queryPartitionStatistics.Update(_physicalPartitionInfo.PhysicalPartitionCount);
                    }
                    else
                    {
                        // determine the number of unique physical partitions queried as part of this search.
                        int physicalPartitionCount = messagesList.Select(r => r.Headers["x-ms-documentdb-partitionkeyrangeid"]).Distinct().Count();
                        queryPartitionStatistics.Update(physicalPartitionCount);
                    }
                }

                return (results, nextContinuationToken, feedOptions.MaxConcurrency);
            }
            finally
            {
                if (queryPartitionStatistics != null && fhirRequestContext != null)
                {
                    fhirRequestContext.Properties.Remove(Constants.CosmosDbResponseMessagesProperty);
                }
            }
        }

        /// <summary>
        /// Heuristic for determining whether a query is selective or not. If it is, we should query partitions in parallel.
        /// If it is not, we should query sequentially, since we would expect to get a full page of results from the first partition.
        /// This is really simple right now
        /// </summary>
        private bool IsQuerySelective(QueryPartitionStatistics queryPartitionStatistics)
        {
            int? averagePartitionCount = queryPartitionStatistics.GetAveragePartitionCount();

            if (averagePartitionCount.HasValue && _cosmosConfig.UseQueryStatistics)
            {
                // this is not a new query

                double fractionOfPartitionsHit = (double)averagePartitionCount.Value / _physicalPartitionInfo.PhysicalPartitionCount;

                if (fractionOfPartitionsHit >= 0.5)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<int> ExecuteCountSearchAsync(
            QueryDefinition sqlQuerySpec,
            CancellationToken cancellationToken)
        {
            var feedOptions = new QueryRequestOptions
            {
                MaxConcurrency = CosmosFhirDataStore.MaxQueryConcurrency, // execute counts across all partitions
            };

            return (await _fhirDataStore.ExecuteDocumentQueryAsync<int>(sqlQuerySpec, feedOptions, continuationToken: null, cancellationToken: cancellationToken)).results.Single();
        }

        private SearchResult CreateSearchResult(SearchOptions searchOptions, IEnumerable<SearchResultEntry> results, string continuationToken, bool includesTruncated = false)
        {
            if (includesTruncated)
            {
                _requestContextAccessor.FhirRequestContext.BundleIssues.Add(
                    new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Warning,
                        OperationOutcomeConstants.IssueType.Incomplete,
                        Core.Resources.TruncatedIncludeMessage));
            }

            return new SearchResult(
                results,
                continuationToken,
                searchOptions.Sort,
                searchOptions.UnsupportedSearchParams);
        }

        private async Task<(IList<FhirCosmosResourceWrapper> includes, bool includesTruncated)> PerformIncludeQueries(
            IReadOnlyList<FhirCosmosResourceWrapper> matches,
            IReadOnlyCollection<IncludeExpression> includeExpressions,
            IReadOnlyCollection<IncludeExpression> revIncludeExpressions,
            int maxIncludeCount,
            CancellationToken cancellationToken)
        {
            if (matches.Count == 0 || (includeExpressions.Count == 0 && revIncludeExpressions.Count == 0))
            {
                return (Array.Empty<FhirCosmosResourceWrapper>(), false);
            }

            var includes = new List<FhirCosmosResourceWrapper>();

            if (includeExpressions.Count > 0)
            {
                // fetch in the resources to include from _include parameters.

                var referencesToInclude = matches
                    .SelectMany(m => m.ReferencesToInclude)
                    .Where(r => r.ResourceTypeName != null) // exclude untyped references to align with the current SQL behavior
                    .Distinct()
                    .ToList();

                foreach (IEnumerable<ResourceTypeAndId> resourceTypeAndIds in referencesToInclude.TakeBatch(maxIncludeCount))
                {
                    // issue the requests in parallel
                    var tasks = resourceTypeAndIds.Select(r => _fhirDataStore.GetAsync(new ResourceKey(r.ResourceTypeName, r.ResourceId), cancellationToken)).ToList();

                    foreach (Task<ResourceWrapper> task in tasks)
                    {
                        var resourceWrapper = (FhirCosmosResourceWrapper)await task;
                        if (resourceWrapper != null)
                        {
                            includes.Add(resourceWrapper);
                            if (includes.Count > maxIncludeCount)
                            {
                                return (includes, true);
                            }
                        }
                    }
                }
            }

            bool includesTruncated = false;
            if (revIncludeExpressions.Count > 0)
            {
                // fetch in the resources to include from _revinclude parameters.

                foreach (IncludeExpression revIncludeExpression in revIncludeExpressions)
                {
                    // construct the expression resourceType = <SourceResourceType> AND (referenceSearchParameter = <MatchResourceType1, MatchResourceId1> OR referenceSearchParameter = <MatchResourceType2, MatchResourceId2> OR ...)

                    SearchParameterExpression sourceTypeExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, revIncludeExpression.SourceResourceType));
                    SearchParameterInfo referenceSearchParameter = revIncludeExpression.WildCard ? _wildcardReferenceSearchParameter : revIncludeExpression.ReferenceSearchParameter;
                    SearchParameterExpression referenceExpression = Expression.SearchParameter(
                        referenceSearchParameter,
                        Expression.Or(
                            matches
                                .GroupBy(m => m.ResourceTypeName)
                                .Select(g =>
                                    Expression.And(
                                        Expression.Equals(FieldName.ReferenceResourceType, null, g.Key),
                                        Expression.Or(g.Select(m => Expression.Equals(FieldName.ReferenceResourceId, null, m.ResourceId)).ToList()))).ToList()));

                    Expression expression = Expression.And(sourceTypeExpression, referenceExpression);

                    var revIncludeSearchOptions = new SearchOptions
                    {
                        Expression = expression,
                        Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                    };

                    QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);

                    (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, int? maxConcurrency) includeResponse = default;

                    for (int i = 0; i == 0 || !string.IsNullOrEmpty(includeResponse.continuationToken); i++)
                    {
                        // inflate the item count so that we we get at least maxIncludeCount - includes.Count results back
                        revIncludeSearchOptions.MaxItemCount = (int)((maxIncludeCount - includes.Count) / CosmosFhirDataStore.ExecuteDocumentQueryAsyncMinimumFillFactor);

                        switch (i)
                        {
                            case 0:

                                // First time around. The query may or may not execute in parallel depending on past performance. If it is not going to execute in parallel, we give it 5 seconds before cancelling. After that, we will force parallelism.

                                includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                                    revIncludeQuery,
                                    revIncludeSearchOptions,
                                    null,
                                    TimeSpan.FromSeconds(5),
                                    queryRequestOptionsOverride: null,
                                    cancellationToken);

                                // check if we will restart the query in the next iteration (see next case below)
                                if (includeResponse.continuationToken == null || includeResponse.maxConcurrency != null)
                                {
                                    includes.AddRange(includeResponse.results);
                                }

                                break;

                            case 1 when includeResponse.maxConcurrency == null:

                                // The previous iteration executed sequentially and did not retrieve the desired number of results. We will switch to parallel execution.
                                // Note that we are not passing in the continuation token, because if we do, the SDK does not execute in parallel.

                                var queryRequestOptionsOverride = new QueryRequestOptions { MaxItemCount = revIncludeSearchOptions.MaxItemCount, MaxConcurrency = CosmosFhirDataStore.MaxQueryConcurrency };

                                includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                                    revIncludeQuery,
                                    revIncludeSearchOptions,
                                    null,
                                    null,
                                    queryRequestOptionsOverride,
                                    cancellationToken);

                                includes.AddRange(includeResponse.results);
                                break;

                            default:

                                // follow the continuation

                                includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                                    revIncludeQuery,
                                    revIncludeSearchOptions,
                                    includeResponse.continuationToken,
                                    null,
                                    null,
                                    cancellationToken);

                                includes.AddRange(includeResponse.results);
                                break;
                        }

                        if (includes.Count >= maxIncludeCount)
                        {
                            int toRemove = includes.Count - maxIncludeCount;
                            includes.RemoveRange(includes.Count - toRemove, toRemove);
                            includesTruncated = true;
                            break;
                        }
                    }
                }
            }

            return (includes, includesTruncated);
        }

        private static bool ExtractIncludeAndChainedExpressions(
            Expression inputExpression,
            out Expression expressionWithoutIncludesOrChained,
            out IReadOnlyList<IncludeExpression> includeExpressions,
            out IReadOnlyList<IncludeExpression> revIncludeExpressions,
            out IReadOnlyList<ChainedExpression> chainedExpressions)
        {
            switch (inputExpression)
            {
                case IncludeExpression ie when ie.Reversed:
                    expressionWithoutIncludesOrChained = null;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = new[] { ie };
                    chainedExpressions = Array.Empty<ChainedExpression>();
                    return true;
                case IncludeExpression ie:
                    expressionWithoutIncludesOrChained = null;
                    includeExpressions = new[] { ie };
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    chainedExpressions = Array.Empty<ChainedExpression>();
                    return true;
                case ChainedExpression ie:
                    expressionWithoutIncludesOrChained = null;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    chainedExpressions = new[] { ie };
                    return true;
                case MultiaryExpression me when me.Expressions.Any(e => e is IncludeExpression || e is ChainedExpression):
                    includeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => !ie.Reversed).ToList();
                    revIncludeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => ie.Reversed).ToList();
                    chainedExpressions = me.Expressions.OfType<ChainedExpression>().ToList();
                    expressionWithoutIncludesOrChained = (me.Expressions.Count - (includeExpressions.Count + revIncludeExpressions.Count + chainedExpressions.Count)) switch
                    {
                        0 => null,
                        1 => me.Expressions.Single(e => !(e is IncludeExpression || e is ChainedExpression)),
                        _ => new MultiaryExpression(me.MultiaryOperation, me.Expressions.Where(e => !(e is IncludeExpression || e is ChainedExpression)).ToList()),
                    };
                    return true;
                default:
                    expressionWithoutIncludesOrChained = inputExpression;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    chainedExpressions = Array.Empty<ChainedExpression>();
                    return false;
            }
        }
    }
}
