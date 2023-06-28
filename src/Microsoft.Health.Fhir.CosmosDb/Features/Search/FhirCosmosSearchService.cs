// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Utility;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly CosmosDataStoreConfiguration _cosmosConfig;
        private readonly ICosmosDbCollectionPhysicalPartitionInfo _physicalPartitionInfo;
        private readonly QueryPartitionStatisticsCache _queryPartitionStatisticsCache;
        private readonly Lazy<IReadOnlyCollection<IExpressionVisitorWithInitialContext<object, Expression>>> _expressionRewriters;
        private readonly ILogger<FhirCosmosSearchService> _logger;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly SearchParameterInfo _resourceIdSearchParameter;
        private const int _chainedSearchMaxSubqueryItemLimit = 1000;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            CosmosDataStoreConfiguration cosmosConfig,
            ICosmosDbCollectionPhysicalPartitionInfo physicalPartitionInfo,
            QueryPartitionStatisticsCache queryPartitionStatisticsCache,
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            ILogger<FhirCosmosSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(cosmosConfig, nameof(cosmosConfig));
            EnsureArg.IsNotNull(physicalPartitionInfo, nameof(physicalPartitionInfo));
            EnsureArg.IsNotNull(queryPartitionStatisticsCache, nameof(queryPartitionStatisticsCache));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _requestContextAccessor = requestContextAccessor;
            _cosmosConfig = cosmosConfig;
            _physicalPartitionInfo = physicalPartitionInfo;
            _queryPartitionStatisticsCache = queryPartitionStatisticsCache;
            _logger = logger;
            _resourceTypeSearchParameter = SearchParameterInfo.ResourceTypeSearchParameter;
            _resourceIdSearchParameter = new SearchParameterInfo(SearchParameterNames.Id, SearchParameterNames.Id);

            _expressionRewriters = new Lazy<IReadOnlyCollection<IExpressionVisitorWithInitialContext<object, Expression>>>(() =>
                new IExpressionVisitorWithInitialContext<object, Expression>[]
                {
                    compartmentSearchRewriter,
                    smartCompartmentSearchRewriter,
                    DateTimeEqualityRewriter.Instance,
                });
        }

        public override async Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            var sqlQuerySpec = new QueryDefinition(@"SELECT DISTINCT VALUE r.resourceTypeName
                FROM root r
                WHERE r.isSystem = false");

            var requestOptions = new QueryRequestOptions();

            return await _fhirDataStore.ExecutePagedQueryAsync<string>(sqlQuerySpec, requestOptions, cancellationToken: cancellationToken);
        }

        public override async Task<SearchResult> SearchAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            // we're going to mutate searchOptions, so clone it first so the caller of this method does not see the changes.
            searchOptions = searchOptions.Clone();

            if (searchOptions.Expression != null)
            {
                // Apply Cosmos specific expression rewriters
                searchOptions.Expression = _expressionRewriters.Value
                    .Aggregate(searchOptions.Expression, (e, rewriter) => e.AcceptVisitor(rewriter));
            }

            // pull out the _include and _revinclude expressions.
            bool hasIncludeOrRevIncludeExpressions = searchOptions.Expression.ExtractIncludeAndChainedExpressions(
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

        public override async IAsyncEnumerable<(DateTimeOffset StartTime, DateTimeOffset EndTime)> GetResourceTimeRanges(
            string resourceType,
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            int rangeSize,
            int numberOfRanges,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Using PartialDateTime in parameters ensures dates are formatted correctly when converted to string.
            QueryDefinition sqlQuerySpec = new QueryDefinition(@"SELECT VALUE r.lastModified
                FROM root r
                WHERE r.isSystem = false
                AND r.isHistory = false
                AND r.resourceTypeName = @resourceTypeName
                AND r.lastModified >= @startTime
                AND r.lastModified <= @endTime
                ORDER BY r.lastModified")
               .WithParameter("@resourceTypeName", resourceType)
               .WithParameter("@startTime", new PartialDateTime(startTime).ToString())
               .WithParameter("@endTime", new PartialDateTime(endTime).ToString());

            var requestOptions = new QueryRequestOptions();
            
            // requestOptions.MaxItemCount = rangeSize * numberOfRanges;

            string continuationToken = null;
            IReadOnlyList<DateTimeOffset> results = null;
            int index = 0;
            DateTimeOffset? currentStart = null;

            do
            {
                // Only fetch new results when we've exhausted the previous results
                if (results is null || index >= results.Count)
                {
                    if (index > 0)
                    {
                        index = index % results.Count;
                    }

                    // #TODO - do we need error handling here?
                    (results, continuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<DateTimeOffset>(sqlQuerySpec, requestOptions, continuationToken, cancellationToken: cancellationToken);
                }

                while (index < results.Count)
                {
                    if (currentStart is null && results.Any())
                    {
                        currentStart = results[index];
                    }

                    // Skip forward to the next boundry.
                    index += rangeSize - 1;

                    if (index < results.Count)
                    {
                        yield return (currentStart.Value, results[index]);
                        currentStart = null;
                        index++;
                    }
                }
            }
            while (continuationToken is not null);

            // Add any partially full range at the end
            if (currentStart is not null)
            {
                yield return (currentStart.Value, results[results.Count - 1]);
            }
        }

        private async Task<List<Expression>> PerformChainedSearch(SearchOptions searchOptions, IReadOnlyList<ChainedExpression> chainedExpressions, CancellationToken cancellationToken)
        {
            var chainedReferences = new List<Expression>();
            SearchOptions chainedOptions = searchOptions.Clone();
            chainedOptions.CountOnly = false;
            chainedOptions.MaxItemCount = _chainedSearchMaxSubqueryItemLimit;
            chainedOptions.MaxItemCountSpecifiedByClient = false;
            chainedOptions.Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>();

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

            var chainedResults = new List<FhirCosmosResourceWrapper>();

            try
            {
                // Because ExecuteSubQueryAsync will continue to search until the result set is filled, set a time-limit for this part of the chained expression
                using var queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                queryTimeout.CancelAfter(TimeSpan.FromSeconds(15));

                if (!await ExecuteSubQueryAsync(
                    chainedResults,
                    _queryBuilder.BuildSqlQuerySpec(chainedOptions, new QueryBuilderOptions(includeExpressions, projection: includeExpressions.Any() ? QueryProjection.ReferencesOnly : QueryProjection.IdAndType)),
                    _chainedSearchMaxSubqueryItemLimit,
                    chainedOptions,
                    queryTimeout.Token))
                {
                    throw new InvalidSearchOperationException(string.Format(CultureInfo.InvariantCulture, Resources.ChainedExpressionSubqueryLimit, _chainedSearchMaxSubqueryItemLimit));
                }
            }
            catch (OperationCanceledException)
            {
                throw new RequestTooCostlyException(Core.Resources.ConditionalRequestTooCostly);
            }

            if (!chainedResults.Any())
            {
                return null;
            }

            if (expression.Reversed)
            {
                // When reverse chained, we take the ids and types from the child object and use it to filter the parent objects.
                // e.g. Patient?_has:Group:member:_id=group1. In this case we would have run the query there Group.id = group1
                // and returned the indexed entries for Group.member. The following query will use these items to filter the parent Patient query.

                List<ResourceTypeAndId> resourceTypeAndIds = chainedResults.SelectMany(x => x.ReferencesToInclude.Select(y => y)).Distinct().ToList();

                if (!resourceTypeAndIds.Any())
                {
                    return null;
                }

                List<MultiaryExpression> typeAndResourceExpressions = resourceTypeAndIds
                    .GroupBy(x => x.ResourceTypeName)
                    .Select(g =>
                        Expression.And(
                            Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, g.Key)),
                            Expression.SearchParameter(_resourceIdSearchParameter, Expression.In(FieldName.TokenCode, null, g.Select(x => x.ResourceId))))).ToList();

                return typeAndResourceExpressions.Count == 1 ? typeAndResourceExpressions.First() : Expression.Or(typeAndResourceExpressions.ToArray());
            }

            // When normal chained expression we can filter using references in the parent object. e.g. Observation.subject
            // The following expression constrains "subject" references on "Observation" with the ids that have matched the sub-query

            return Expression.SearchParameter(
                expression.ReferenceSearchParameter,
                Expression.Or(
                    chainedResults
                        .GroupBy(m => m.ResourceTypeName)
                        .Select(g =>
                            Expression.And(
                                Expression.Equals(FieldName.ReferenceResourceType, null, g.Key),
                                Expression.In(FieldName.ReferenceResourceId, null, g.Select(x => x.ResourceId)))).ToList()));
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
            ConcurrentBag<CosmosResponseMessage> messagesList = null;
            if (_physicalPartitionInfo.PhysicalPartitionCount > 1 && queryRequestOptionsOverride == null)
            {
                if (searchOptions.Sort?.Count > 0)
                {
                    feedOptions.MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency;
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
                            feedOptions.MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency;
                        }

                        // plant a ConcurrentBag int the request context's properties, so the CosmosResponseProcessor
                        // knows to add the individual ResponseMessages sent as part of this search.

                        fhirRequestContext = _requestContextAccessor.RequestContext;
                        if (fhirRequestContext != null)
                        {
                            messagesList = new ConcurrentBag<CosmosResponseMessage>();
                            fhirRequestContext.Properties[Constants.CosmosDbResponseMessagesProperty] = messagesList;
                        }
                    }
                }
            }

            try
            {
                (IReadOnlyList<T> results, string nextContinuationToken) = (null, null);
                var desiredItemCount = feedOptions.MaxItemCount * CosmosFhirDataStore.ExecuteDocumentQueryAsyncMinimumFillFactor;

                // Set timeout for sequential query execution
                if (feedOptions.MaxConcurrency == null &&
                    _cosmosConfig.ParallelQueryOptions.EnableConcurrencyIfQueryExceedsTimeLimit == true &&
                    string.IsNullOrEmpty(searchOptions.ContinuationToken) &&
                    string.IsNullOrEmpty(continuationToken) &&
                    searchEnumerationTimeoutOverrideIfSequential == null)
                {
                    searchEnumerationTimeoutOverrideIfSequential = TimeSpan.FromSeconds(5);

                    // Executing query sequentially until the timeout
                    (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, null, searchOptions.MaxItemCountSpecifiedByClient, searchEnumerationTimeoutOverrideIfSequential, cancellationToken);

                    // check if we need to restart the query in parallel
                    if (results.Count < desiredItemCount && !string.IsNullOrEmpty(nextContinuationToken))
                    {
                        feedOptions.MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency;
                        (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, null, searchOptions.MaxItemCountSpecifiedByClient, null, cancellationToken);
                    }
                }
                else
                {
                    (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, continuationToken, searchOptions.MaxItemCountSpecifiedByClient, feedOptions.MaxConcurrency == null ? searchEnumerationTimeoutOverrideIfSequential : null, cancellationToken);
                }

                if (queryPartitionStatistics != null && messagesList != null)
                {
                    if ((results == null || results.Count < desiredItemCount) && !string.IsNullOrEmpty(nextContinuationToken))
                    {
                        // ExecuteDocumentQueryAsync gave up on filling the pages. This suggests that we would have been better off querying in parallel.
                        queryPartitionStatistics.Update(_physicalPartitionInfo.PhysicalPartitionCount);

                        _logger.LogInformation(
                            "Failed to fill items, found {ItemCount}, needed {DesiredItemCount}. Updating statistics to {PhysicalPartitionCount}",
                            results.Count,
                            desiredItemCount,
                            _physicalPartitionInfo.PhysicalPartitionCount);
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
                    _logger.LogInformation(
                        "Query was Selective. Avg. Partitions: {AvgPartitions} / Physical Partitions: {PhysicalPartitionCount} = {FractionOfPartitionsHit}",
                        averagePartitionCount.Value,
                        _physicalPartitionInfo.PhysicalPartitionCount,
                        fractionOfPartitionsHit);

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
                MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency, // execute counts across all partitions
            };

            long count = (await _fhirDataStore.ExecuteDocumentQueryAsync<long>(sqlQuerySpec, feedOptions, continuationToken: null, cancellationToken: cancellationToken)).results.Single();
            if (count > int.MaxValue)
            {
                _requestContextAccessor.RequestContext.BundleIssues.Add(
                    new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue)));

                throw new InvalidSearchOperationException(string.Format(Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue));
            }

            return (int)count;
        }

        private SearchResult CreateSearchResult(SearchOptions searchOptions, IEnumerable<SearchResultEntry> results, string continuationToken, bool includesTruncated = false)
        {
            if (includesTruncated)
            {
                _requestContextAccessor.RequestContext.BundleIssues.Add(
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
            var matchIds = matches.Select(x => new ResourceKey(x.ResourceTypeName, x.ResourceId)).ToHashSet();

            if (includeExpressions.Count > 0)
            {
                // fetch in the resources to include from _include parameters.

                var referencesToInclude = matches
                    .SelectMany(m => m.ReferencesToInclude)
                    .Where(r => r.ResourceTypeName != null) // exclude untyped references to align with the current SQL behavior
                    .Select(x => new ResourceKey(x.ResourceTypeName, x.ResourceId))
                    .Distinct()
                    .Where(x => !matchIds.Contains(x))
                    .ToList();

                foreach (IEnumerable<ResourceKey> resourceTypeAndIds in referencesToInclude.TakeBatch(maxIncludeCount))
                {
                    // issue the requests in parallel
                    var tasks = resourceTypeAndIds.Select(r => _fhirDataStore.GetAsync(r, cancellationToken)).ToList();

                    foreach (Task<ResourceWrapper> task in tasks)
                    {
                        var resourceWrapper = (FhirCosmosResourceWrapper)await task;

                        // Get Async always return latest resource, so no need to check for history flag.
                        if (resourceWrapper != null && !resourceWrapper.IsDeleted)
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

                    List<IGrouping<string, FhirCosmosResourceWrapper>> matchesGroupedByType = matches.GroupBy(m => m.ResourceTypeName).ToList();

                    Expression referenceExpression = Expression.And(
                        Expression.SearchParameter(
                            referenceSearchParameter,
                            Expression.Or(matchesGroupedByType
                                    .Select(g =>
                                        Expression.And(
                                            Expression.Equals(FieldName.ReferenceResourceType, null, g.Key),
                                            Expression.In(FieldName.ReferenceResourceId, null, g.Select(x => x.ResourceId)))).ToList())),
                        /* This part of the expression ensures that the reference isn't the same as a resource that has already been selected as a match */
                        Expression.Not(Expression.Or(matchesGroupedByType
                            .Select(g =>
                                Expression.And(
                                    Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, g.Key, false)),
                                    Expression.SearchParameter(_resourceIdSearchParameter, Expression.In(FieldName.TokenCode, null, g.Select(x => x.ResourceId))))).ToList())));

                    Expression expression = Expression.And(sourceTypeExpression, referenceExpression);

                    var revIncludeSearchOptions = new SearchOptions
                    {
                        Expression = expression,
                        Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                    };

                    QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);

                    includesTruncated = !await ExecuteSubQueryAsync(includes, revIncludeQuery, maxIncludeCount, revIncludeSearchOptions, cancellationToken);
                }
            }

            return (includes, includesTruncated);
        }

        /// <summary>
        /// Aggressively searches for results as part of server-side sub-queries
        /// </summary>
        /// <returns>True if results were within MaxCount. False if results were truncated</returns>
        private async Task<bool> ExecuteSubQueryAsync(List<FhirCosmosResourceWrapper> includes, QueryDefinition query, int maxCount, SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, int? maxConcurrency) includeResponse = default;

            for (int i = 0; i == 0 || !string.IsNullOrEmpty(includeResponse.continuationToken); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // inflate the item count so that we we get at least maxIncludeCount - includes.Count results back
                searchOptions.MaxItemCount = (int)((maxCount - includes.Count) / CosmosFhirDataStore.ExecuteDocumentQueryAsyncMinimumFillFactor);

                switch (i)
                {
                    case 0:

                        // First time around. The query may or may not execute in parallel depending on past performance. If it is not going to execute in parallel, we give it 5 seconds before cancelling. After that, we will force parallelism.

                        includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                            query,
                            searchOptions,
                            null,
                            TimeSpan.FromSeconds(5),
                            queryRequestOptionsOverride: null,
                            cancellationToken);

                        // check if we will restart the query in the next iteration (see next case below)
                        // if not, take the results now
                        if (includeResponse.continuationToken == null || includeResponse.maxConcurrency != null || includeResponse.results.Count >= maxCount)
                        {
                            includes.AddRange(includeResponse.results);
                        }

                        break;

                    case 1 when includeResponse.maxConcurrency == null:

                        // The previous iteration executed sequentially and did not retrieve the desired number of results. We will switch to parallel execution.
                        // Note that we are not passing in the continuation token, because if we do, the SDK does not execute in parallel.

                        var queryRequestOptionsOverride = new QueryRequestOptions { MaxItemCount = searchOptions.MaxItemCount, MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency };

                        includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                            query,
                            searchOptions,
                            null,
                            null,
                            queryRequestOptionsOverride,
                            cancellationToken);

                        includes.AddRange(includeResponse.results);
                        break;

                    default:

                        // follow the continuation

                        includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                            query,
                            searchOptions,
                            includeResponse.continuationToken,
                            null,
                            null,
                            cancellationToken);

                        includes.AddRange(includeResponse.results);
                        break;
                }

                if (includes.Count > maxCount)
                {
                    int toRemove = includes.Count - maxCount;
                    includes.RemoveRange(includes.Count - toRemove, toRemove);

                    // break from the for loop because enough results have been found
                    return false;
                }
            }

            return true;
        }
    }
}
