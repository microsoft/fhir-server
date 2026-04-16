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
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
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
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            ILogger<FhirCosmosSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore, logger)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(cosmosConfig, nameof(cosmosConfig));
            EnsureArg.IsNotNull(physicalPartitionInfo, nameof(physicalPartitionInfo));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _requestContextAccessor = requestContextAccessor;
            _cosmosConfig = cosmosConfig;
            _physicalPartitionInfo = physicalPartitionInfo;
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

        public override async Task<IEnumerable<string>> GetFeedRanges(CancellationToken cancellationToken)
        {
            var ranges = await _fhirDataStore.GetFeedRanges(cancellationToken);
            return ranges.Select(x => x.ToJsonString());
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

            // pull out the _include and _revinclude expressions, and extract SMART v2 scope expression if present
            bool hasIncludeOrRevIncludeExpressions = searchOptions.Expression.ExtractIncludeAndChainedExpressions(
                out Expression expressionWithoutIncludes,
                out IReadOnlyList<IncludeExpression> includeExpressions,
                out IReadOnlyList<IncludeExpression> revIncludeExpressions,
                out IReadOnlyList<ChainedExpression> chainedExpressions,
                out IReadOnlyList<UnionExpression> smartV2ScopeExpressions);

            if (hasIncludeOrRevIncludeExpressions)
            {
                searchOptions.Expression = expressionWithoutIncludes;

                if (includeExpressions.Any(e => e.Iterate) || revIncludeExpressions.Any(e => e.Iterate))
                {
                    // We haven't implemented this yet.
                    _logger.LogWarning("Bad Request (IncludeIterateNotSupported)");
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
                _queryBuilder.BuildSqlQuerySpec(searchOptions, new QueryBuilderOptions(includeExpressions, searchOptions.OnlyIds ? QueryProjection.IdAndType : QueryProjection.Default)),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                null,
                null,
                cancellationToken);

            (IList<FhirCosmosResourceWrapper> includes, bool includesTruncated) = await PerformIncludeQueries(results, includeExpressions, revIncludeExpressions, searchOptions.IncludeCount, smartV2ScopeExpressions, cancellationToken);

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

        public override Task<SearchResult> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false)
        {
            if (isIncludesOperation)
            {
                throw new SearchOperationNotSupportedException(Fhir.Core.Resources.UnsupportedIncludesOperation);
            }

            return base.SearchAsync(resourceType, queryParameters, cancellationToken, isAsyncOperation, resourceVersionTypes, onlyIds, isIncludesOperation);
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
                    _logger.LogWarning("Invalid Search Operation (ChainedExpressionSubqueryLimit)");
                    throw new InvalidSearchOperationException(string.Format(CultureInfo.InvariantCulture, Resources.ChainedExpressionSubqueryLimit, _chainedSearchMaxSubqueryItemLimit));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request Too Costly (ConditionalRequestTooCostly)");
                throw new RequestTooCostlyException(Microsoft.Health.Fhir.Core.Resources.ConditionalRequestTooCostly);
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

            IFhirRequestContext fhirRequestContext = null;
            ConcurrentBag<CosmosResponseMessage> messagesList = null;
            if (queryRequestOptionsOverride == null)
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
                        // Telemetry currently shows that when there is a continuation token, then the query only hits one partition.
                        // This may not be true forever, in which case we would want to encode the max concurrency in the continuation token.

                        fhirRequestContext = _requestContextAccessor.RequestContext;
                        if (fhirRequestContext != null)
                        {
                            // Check if the "Optimize Concurrency" flag is present in the FHIR context, then Cosmos DB
                            // will be able to maximize the number of concurrent operations.
                            if (fhirRequestContext.Properties.TryGetValue(KnownQueryParameterNames.OptimizeConcurrency, out object maxParallelAsObject))
                            {
                                if (maxParallelAsObject != null && Convert.ToBoolean(maxParallelAsObject))
                                {
                                    feedOptions.MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency;
                                }
                            }

                            // Plant a ConcurrentBag int the request context's properties, so the CosmosResponseProcessor
                            // knows to add the individual ResponseMessages sent as part of this search.
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

                // Feed ranges should only be used in async operations, but defining for all queries in case unknown code paths are hit.
                FeedRange queryFeedRange = null;
                try
                {
                    if (searchOptions.FeedRange is not null)
                    {
                        queryFeedRange = FeedRange.FromJsonString(searchOptions.FeedRange);
                    }
                }
                catch (ArgumentException)
                {
                    _logger.LogWarning("Bad Request (InvalidFeedRange)");
                    throw new BadRequestException(Resources.InvalidFeedRange);
                }

                // Set timeout for sequential query execution
                if (feedOptions.MaxConcurrency == null &&
                    _cosmosConfig.ParallelQueryOptions.EnableConcurrencyIfQueryExceedsTimeLimit == true &&
                    string.IsNullOrEmpty(searchOptions.ContinuationToken) &&
                    string.IsNullOrEmpty(continuationToken) &&
                    searchEnumerationTimeoutOverrideIfSequential == null &&
                    searchOptions.IsAsyncOperation != true)
                {
                    searchEnumerationTimeoutOverrideIfSequential = TimeSpan.FromSeconds(5);

                    // Executing query sequentially until the timeout
                    (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(
                        sqlQuerySpec: sqlQuerySpec,
                        feedOptions: feedOptions,
                        feedRange: queryFeedRange,
                        mustNotExceedMaxItemCount: searchOptions.MaxItemCountSpecifiedByClient,
                        searchEnumerationTimeoutOverride: searchEnumerationTimeoutOverrideIfSequential,
                        cancellationToken: cancellationToken);

                    // check if we need to restart the query in parallel
                    if (results.Count < desiredItemCount && !string.IsNullOrEmpty(nextContinuationToken))
                    {
                        feedOptions.MaxConcurrency = _cosmosConfig.ParallelQueryOptions.MaxQueryConcurrency;
                        (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(
                            sqlQuerySpec: sqlQuerySpec,
                            feedOptions: feedOptions,
                            feedRange: queryFeedRange,
                            mustNotExceedMaxItemCount: searchOptions.MaxItemCountSpecifiedByClient,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    var mustNotExceedMaxItemCount = searchOptions.MaxItemCountSpecifiedByClient;

                    // Async operations also need the full result set. Accounting for the till factor to achieve that.
                    if (searchOptions.IsAsyncOperation)
                    {
                        mustNotExceedMaxItemCount = false;
                        feedOptions.MaxItemCount = (int)(feedOptions.MaxItemCount / CosmosFhirDataStore.ExecuteDocumentQueryAsyncMinimumFillFactor);
                    }

                    (results, nextContinuationToken) = await _fhirDataStore.ExecuteDocumentQueryAsync<T>(
                        sqlQuerySpec: sqlQuerySpec,
                        feedOptions: feedOptions,
                        feedRange: queryFeedRange,
                        continuationToken: continuationToken,
                        mustNotExceedMaxItemCount: mustNotExceedMaxItemCount,
                        searchEnumerationTimeoutOverride: feedOptions.MaxConcurrency == null ? searchEnumerationTimeoutOverrideIfSequential : null,
                        cancellationToken: cancellationToken);
                }

                if (messagesList != null)
                {
                    if ((results == null || results.Count < desiredItemCount) && !string.IsNullOrEmpty(nextContinuationToken))
                    {
                        // ExecuteDocumentQueryAsync gave up on filling the pages. This suggests that we would have been better off querying in parallel.

                        _logger.LogWarning(
                            "Failed to fill items, found {ItemCount}, needed {DesiredItemCount}. Physical partition count {PhysicalPartitionCount}",
                            results.Count,
                            desiredItemCount,
                            _physicalPartitionInfo.PhysicalPartitionCount);
                    }
                }

                return (results, nextContinuationToken, feedOptions.MaxConcurrency);
            }
            finally
            {
                if (fhirRequestContext != null)
                {
                    fhirRequestContext.Properties.Remove(Constants.CosmosDbResponseMessagesProperty);
                }
            }
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
                        string.Format(Microsoft.Health.Fhir.Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue)));

                _logger.LogWarning("Invalid Search Operation (SearchCountResultsExceedLimit)");
                throw new InvalidSearchOperationException(string.Format(Microsoft.Health.Fhir.Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue));
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
                        Microsoft.Health.Fhir.Core.Resources.TruncatedIncludeMessage));
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
            IReadOnlyList<UnionExpression> smartV2ScopeExpressions,
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

                // Check if we have SMART v2 granular scopes that require filtering
                bool hasSmartGranularScopes = smartV2ScopeExpressions != null && smartV2ScopeExpressions.Any() &&
                    _requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true;

                if (hasSmartGranularScopes)
                {
                    // Group references by resource type and apply SMART scope filtering via search queries
                    foreach (var resourceTypeGroup in referencesToInclude.GroupBy(r => r.ResourceType))
                    {
                        string resourceType = resourceTypeGroup.Key;
                        Expression smartScopeFilter = GetSmartScopeFilterForResourceType(smartV2ScopeExpressions, resourceType, out bool hasNoAccess);

                        // Skip this resource type if no scope allows access
                        if (hasNoAccess)
                        {
                            continue;
                        }

                        // Build expression: (resourceType = X AND _id IN (id1, id2, ...)) [AND smartScopeFilter]
                        var resourceIds = resourceTypeGroup.Select(x => x.Id).ToList();
                        Expression expression = Expression.And(
                            Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, resourceType)),
                            Expression.SearchParameter(_resourceIdSearchParameter, Expression.In(FieldName.TokenCode, null, resourceIds)));

                        // Apply SMART scope filter if it exists (granular scopes)
                        if (smartScopeFilter != null)
                        {
                            expression = Expression.And(expression, smartScopeFilter);
                        }

                        var includeSearchOptions = new SearchOptions
                        {
                            Expression = expression,
                            Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                        };

                        QueryDefinition includeQuery = _queryBuilder.BuildSqlQuerySpec(includeSearchOptions);
                        bool includesTruncated = !await ExecuteSubQueryAsync(includes, includeQuery, maxIncludeCount, includeSearchOptions, cancellationToken);

                        if (includesTruncated)
                        {
                            return (includes, true);
                        }
                    }
                }
                else
                {
                    // No SMART granular scopes - use simple GetAsync for better performance
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
            }

            bool revIncludesTruncated = false;
            if (revIncludeExpressions.Count > 0)
            {
                // fetch in the resources to include from _revinclude parameters.

                // Check if we have SMART v2 granular scopes that require filtering
                bool hasSmartGranularScopes = smartV2ScopeExpressions != null && smartV2ScopeExpressions.Any() &&
                    _requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true;

                foreach (IncludeExpression revIncludeExpression in revIncludeExpressions)
                {
                    // construct the expression resourceType = <SourceResourceType> AND (referenceSearchParameter = <MatchResourceType1, MatchResourceId1> OR referenceSearchParameter = <MatchResourceType2, MatchResourceId2> OR ...)

                    if (hasSmartGranularScopes)
                    {
                        // SMART v2 granular scopes logic - apply filtering
                        // Determine the target resource type(s) for this revinclude
                        string[] targetResourceTypes = revIncludeExpression.SourceResourceType == "*"
                            ? (revIncludeExpression.AllowedResourceTypesByScope != null && revIncludeExpression.AllowedResourceTypesByScope.Any() && !revIncludeExpression.AllowedResourceTypesByScope.Contains("all")
                                ? revIncludeExpression.AllowedResourceTypesByScope.ToArray()
                                : null) // wildcard with no scope restrictions
                            : new[] { revIncludeExpression.SourceResourceType };

                        // For wildcard includes (*:*), check SMART scope filter
                        if (targetResourceTypes == null)
                    {
                        // Wildcard revinclude - apply the entire SMART scope expression if present
                        Expression smartScopeFilter = smartV2ScopeExpressions != null && smartV2ScopeExpressions.Any()
                            ? smartV2ScopeExpressions[0]
                            : null;

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

                        Expression expression = smartScopeFilter != null
                            ? Expression.And(smartScopeFilter, referenceExpression)
                            : referenceExpression;

                        var revIncludeSearchOptions = new SearchOptions
                        {
                            Expression = expression,
                            Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                        };

                        QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);
                        revIncludesTruncated = !await ExecuteSubQueryAsync(includes, revIncludeQuery, maxIncludeCount, revIncludeSearchOptions, cancellationToken);
                    }
                    else
                    {
                        // Specific resource type(s) - check scope for each type
                        foreach (string targetResourceType in targetResourceTypes)
                        {
                            Expression smartScopeFilter = GetSmartScopeFilterForResourceType(smartV2ScopeExpressions, targetResourceType, out bool hasNoAccess);

                            // Skip this revinclude if no scope allows access to this resource type
                            if (hasNoAccess)
                            {
                                continue;
                            }

                            SearchParameterExpression sourceTypeExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, targetResourceType));
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

                            // Apply SMART scope filter if it exists
                            if (smartScopeFilter != null)
                            {
                                expression = Expression.And(expression, smartScopeFilter);
                            }

                            var revIncludeSearchOptions = new SearchOptions
                            {
                                Expression = expression,
                                Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                            };

                            QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);
                            revIncludesTruncated = !await ExecuteSubQueryAsync(includes, revIncludeQuery, maxIncludeCount, revIncludeSearchOptions, cancellationToken);
                        }
                    }
                    }
                    else
                    {
                        // No SMART v2 granular scopes - use original revinclude logic
                        // Check SMART scopes
                        SearchParameterExpression sourceTypeExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, revIncludeExpression.SourceResourceType));
                        if (revIncludeExpression.AllowedResourceTypesByScope != null && revIncludeExpression.AllowedResourceTypesByScope.Any() && !revIncludeExpression.AllowedResourceTypesByScope.Contains("all") && revIncludeExpression.SourceResourceType == "*")
                        {
                            sourceTypeExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.In(FieldName.TokenCode, null, revIncludeExpression.AllowedResourceTypesByScope));
                        }

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

                        Expression expression = referenceExpression;

                        // If source type is a not a wildcard, include the sourceTypeExpression in the subquery
                        if ((revIncludeExpression.AllowedResourceTypesByScope != null && revIncludeExpression.AllowedResourceTypesByScope.Any() && !revIncludeExpression.AllowedResourceTypesByScope.Contains("all") && revIncludeExpression.SourceResourceType == "*") || revIncludeExpression.SourceResourceType != "*")
                        {
                            expression = Expression.And(sourceTypeExpression, referenceExpression);
                        }

                        var revIncludeSearchOptions = new SearchOptions
                        {
                            Expression = expression,
                            Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                        };

                        QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);
                        revIncludesTruncated = !await ExecuteSubQueryAsync(includes, revIncludeQuery, maxIncludeCount, revIncludeSearchOptions, cancellationToken);
                    }
                }
            }

            return (includes, revIncludesTruncated);
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
                else if (includes.Count == maxCount && !string.IsNullOrEmpty(includeResponse.continuationToken))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extracts the SMART scope filter expression for a specific resource type from the SMART v2 union expression.
        /// Returns null if no filtering is needed (unrestricted access).
        /// Returns a special "no access" expression if no scope allows access to this resource type (caller should skip the query).
        /// Returns the filter expression if granular scopes exist for this resource type.
        /// </summary>
        private Expression GetSmartScopeFilterForResourceType(IReadOnlyList<UnionExpression> smartV2ScopeExpressions, string resourceType, out bool hasNoAccess)
        {
            hasNoAccess = false;

            if (smartV2ScopeExpressions == null || !smartV2ScopeExpressions.Any())
            {
                return null;
            }

            // Check if SMART scopes should be applied
            if (_requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl != true)
            {
                return null;
            }

            var scopeRestrictions = _requestContextAccessor.RequestContext?.AccessControlContext?.AllowedResourceActions;
            if (scopeRestrictions == null || !scopeRestrictions.Any())
            {
                return null;
            }

            // Check if there's a scope for "all" resources (unrestricted access)
            if (scopeRestrictions.Any(s => s.Resource == KnownResourceTypes.All && s.SearchParameters == null))
            {
                return null; // No filtering needed
            }

            // Check if there's an unrestricted scope for this specific resource type
            var unrestrictedScope = scopeRestrictions.FirstOrDefault(s => s.Resource == resourceType && s.SearchParameters == null);
            if (unrestrictedScope != null)
            {
                return null; // No filtering needed for this resource type
            }

            // Find granular scope(s) for this resource type
            var granularScopes = scopeRestrictions.Where(s => s.Resource == resourceType && s.SearchParameters != null).ToList();
            if (!granularScopes.Any())
            {
                // No scope allows access to this resource type - indicate caller should skip the query
                hasNoAccess = true;
                return null;
            }

            // Extract the filter expressions for this resource type from the union expression
            var unionExpression = smartV2ScopeExpressions[0];
            var resourceTypeFilters = new List<Expression>();

            foreach (var expr in unionExpression.Expressions)
            {
                // Each expression in the union should be an AND expression containing resource type and search parameters
                if (expr is MultiaryExpression andExpr && andExpr.MultiaryOperation == MultiaryOperator.And)
                {
                    // Recursively search for the resource type SearchParameterExpression
                    var resourceTypeExpr = FindResourceTypeExpression(andExpr);

                    if (resourceTypeExpr != null)
                    {
                        // Check if the resource type matches
                        var matchesResourceType = CheckResourceTypeMatches(resourceTypeExpr.Expression, resourceType);
                        if (matchesResourceType)
                        {
                            var singleScopeANDExpressions = new List<Expression>();

                            // Extract just the search parameter filters (not the resource type)
                            // We need to remove the entire sub-expression that contains the resource type filter
                            foreach (MultiaryExpression child in andExpr.Expressions.OfType<MultiaryExpression>())
                            {
                                var searchParamFilters = child.Expressions.Where(e => !ContainsResourceTypeExpression(e)).ToList();
                                if (searchParamFilters.Any())
                                {
                                    singleScopeANDExpressions.Add(searchParamFilters.Count == 1 ? searchParamFilters[0] : Expression.And(searchParamFilters.ToArray()));
                                }
                            }

                            if (singleScopeANDExpressions.Count == 0)
                            {
                                continue;
                            }

                            resourceTypeFilters.Add(singleScopeANDExpressions.Count == 1 ? singleScopeANDExpressions[0] : Expression.And(singleScopeANDExpressions.ToArray()));
                        }
                    }
                }
            }

            if (resourceTypeFilters.Count == 0)
            {
                return null;
            }

            // OR multiple different SCOPES for the same resource type
            return resourceTypeFilters.Count == 1 ? resourceTypeFilters[0] : Expression.Or(resourceTypeFilters.ToArray());
        }

        /// <summary>
        /// Recursively searches for a SearchParameterExpression with Parameter.Name == "_type" or "resourceType"
        /// </summary>
        private static SearchParameterExpression FindResourceTypeExpression(Expression expression)
        {
            if (expression is SearchParameterExpression spe && (spe.Parameter.Name == "_type" || spe.Parameter.Name == "resourceType"))
            {
                return spe;
            }

            if (expression is MultiaryExpression me)
            {
                foreach (var child in me.Expressions)
                {
                    var found = FindResourceTypeExpression(child);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively checks if an expression contains a resource type SearchParameterExpression
        /// </summary>
        private static bool ContainsResourceTypeExpression(Expression expression)
        {
            return FindResourceTypeExpression(expression) != null;
        }

        private static bool CheckResourceTypeMatches(Expression expression, string resourceType)
        {
            // Check if the expression matches the given resource type
            if (expression is StringExpression strExpr && strExpr.Value == resourceType)
            {
                return true;
            }

            return false;
        }
    }
}
