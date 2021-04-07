// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Extensions;
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
        private static readonly SearchParameterInfo _typeIdCompositeSearchParameter = new(SearchValueConstants.TypeIdCompositeSearchParameterName, SearchValueConstants.TypeIdCompositeSearchParameterName);
        private static readonly SearchParameterInfo _wildcardReferenceSearchParameter = new(SearchValueConstants.WildcardReferenceSearchParameterName, SearchValueConstants.WildcardReferenceSearchParameterName);

        private readonly CosmosFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IFhirRequestContextAccessor _requestContextAccessor;
        private readonly CosmosDataStoreConfiguration _cosmosConfig;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly SearchParameterInfo _resourceIdSearchParameter;
        public const string HeaderEnableChainedSearch = "x-ms-enable-chained-search";
        private const int _chainedSearchMaxSubqueryItemLimit = 100;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IFhirRequestContextAccessor requestContextAccessor,
            CosmosDataStoreConfiguration cosmosConfig)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(cosmosConfig, nameof(cosmosConfig));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _requestContextAccessor = requestContextAccessor;
            _cosmosConfig = cosmosConfig;
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

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.BuildSqlQuerySpec(searchOptions, new QueryBuilderOptions(includeExpressions)),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
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
                !(_requestContextAccessor.FhirRequestContext.RequestHeaders.TryGetValue(HeaderEnableChainedSearch, out StringValues featureSetting) &&
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
            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.GenerateHistorySql(searchOptions),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
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

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                queryDefinition,
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                cancellationToken);

            return CreateSearchResult(searchOptions, results.Select(r => new SearchResultEntry(r)), continuationToken);
        }

        private async Task<(IReadOnlyList<T> results, string continuationToken)> ExecuteSearchAsync<T>(
            QueryDefinition sqlQuerySpec,
            SearchOptions searchOptions,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            var feedOptions = new QueryRequestOptions
            {
                MaxItemCount = searchOptions.MaxItemCount,
            };

            return await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, continuationToken, searchOptions.MaxItemCountSpecifiedByClient, cancellationToken: cancellationToken);
        }

        private async Task<int> ExecuteCountSearchAsync(
            QueryDefinition sqlQuerySpec,
            CancellationToken cancellationToken)
        {
            var feedOptions = new QueryRequestOptions
            {
                MaxConcurrency = -1, // execute counts across all partitions
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
            bool includesTruncated = false;

            if (includeExpressions.Count > 0)
            {
                // fetch in the resources to include from _include parameters.

                var referencesToInclude = matches
                    .SelectMany(m => m.ReferencesToInclude)
                    .Where(r => r.ResourceTypeName != null) // exclude untyped references to align with the current SQL behavior
                    .Distinct()
                    .ToList();

                // partition the references to avoid creating an excessively large query
                foreach (IEnumerable<ResourceTypeAndId> batchOfReferencesToInclude in referencesToInclude.TakeBatch(100))
                {
                    // construct the expression typeAndId = <Include1Type, Include1Id> OR  typeAndId = <Include2Type, Include2Id> OR ...

                    SearchParameterExpression expression = Expression.SearchParameter(
                        _typeIdCompositeSearchParameter,
                        Expression.Or(batchOfReferencesToInclude.Select(r =>
                            Expression.And(
                                Expression.Equals(FieldName.TokenCode, 0, r.ResourceTypeName),
                                Expression.Equals(FieldName.TokenCode, 1, r.ResourceId))).ToList()));

                    var includeSearchOptions = new SearchOptions
                    {
                        Expression = expression,
                        MaxItemCount = maxIncludeCount,
                        Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                    };

                    QueryDefinition includeQuery = _queryBuilder.BuildSqlQuerySpec(includeSearchOptions);

                    (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) includeResponse = default;
                    do
                    {
                        if (includes.Count >= maxIncludeCount)
                        {
                            includesTruncated = true;
                            break;
                        }

                        includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                            includeQuery,
                            includeSearchOptions,
                            includeResponse.continuationToken,
                            cancellationToken);
                        includes.AddRange(includeResponse.results);
                    }
                    while (!string.IsNullOrEmpty(includeResponse.continuationToken));

                    if (includes.Count >= maxIncludeCount)
                    {
                        includesTruncated = true;
                        break;
                    }
                }
            }

            if (revIncludeExpressions.Count > 0 && !includesTruncated)
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
                        MaxItemCount = maxIncludeCount - includes.Count,
                        Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                    };

                    QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions);

                    (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) includeResponse = default;
                    do
                    {
                        if (includes.Count >= maxIncludeCount)
                        {
                            includesTruncated = true;
                            break;
                        }

                        var remainingItemsToFind = maxIncludeCount - includes.Count;

                        var queryRequestOptions = new QueryRequestOptions
                        {
                            MaxItemCount = includeResponse.continuationToken == null ? 10 : remainingItemsToFind, // to limit excess buffering in case this is not selective or if there is a continuation token, we know the query will not be done in parallel.
                            MaxConcurrency = int.MaxValue, // This will only execute in parallel on the first iteration of this loop; the SDK ignores this value when there is a continuation token.
                        };

                        includeResponse = await _fhirDataStore.ExecuteDocumentQueryAsync<FhirCosmosResourceWrapper>(
                            revIncludeQuery,
                            queryRequestOptions,
                            includeResponse.continuationToken,
                            false,
                            minimumDesiredPercentage: (int)Math.Ceiling(remainingItemsToFind * 100.0 / queryRequestOptions.MaxItemCount.Value), // ensure we fill up to maxIncludeCount in the initial query, as the SDK does not parallelize queries when there is a continuation token
                            cancellationToken);

                        includes.AddRange(includeResponse.results);
                    }
                    while (!string.IsNullOrEmpty(includeResponse.continuationToken));
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
