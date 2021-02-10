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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class FhirCosmosSearchService : SearchService
    {
        private static readonly SearchParameterInfo _typeIdCompositeSearchParameter = new SearchParameterInfo(SearchValueConstants.TypeIdCompositeSearchParameterName, SearchValueConstants.TypeIdCompositeSearchParameterName);
        private static readonly SearchParameterInfo _wildcardReferenceSearchParameter = new SearchParameterInfo(SearchValueConstants.WildcardReferenceSearchParameterName, SearchValueConstants.WildcardReferenceSearchParameterName);

        private readonly CosmosFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IFhirRequestContextAccessor _requestContextAccessor;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IFhirRequestContextAccessor requestContextAccessor)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _requestContextAccessor = requestContextAccessor;
            _resourceTypeSearchParameter = searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
        }

        protected override async Task<SearchResult> SearchInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            // pull out the _include and _revinclude expressions.
            bool hasIncludeOrRevIncludeExpressions = ExtractIncludeExpressions(
                searchOptions.Expression,
                out Expression expressionWithoutIncludes,
                out IReadOnlyList<IncludeExpression> includeExpressions,
                out IReadOnlyList<IncludeExpression> revIncludeExpressions);

            if (hasIncludeOrRevIncludeExpressions)
            {
                // we're going to mutate searchOptions, so clone it first so the caller of this method does not see the changes.
                searchOptions = searchOptions.Clone();
                searchOptions.Expression = expressionWithoutIncludes;

                if (includeExpressions.Any(e => e.Iterate) || revIncludeExpressions.Any(e => e.Iterate))
                {
                    // We haven't implemented this yet.
                    throw new BadRequestException(Resources.IncludeIterateNotSupported);
                }
            }

            if (searchOptions.CountOnly)
            {
                int count = await ExecuteCountSearchAsync(
                    _queryBuilder.BuildSqlQuerySpec(searchOptions, includes: Array.Empty<IncludeExpression>()),
                    cancellationToken);

                return new SearchResult(count, searchOptions.UnsupportedSearchParams);
            }

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.BuildSqlQuerySpec(searchOptions, includeExpressions),
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
                        _queryBuilder.BuildSqlQuerySpec(searchOptions, includes: Array.Empty<IncludeExpression>()),
                        cancellationToken);
                }
            }

            return searchResult;
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

            return await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, continuationToken, searchOptions.MaxItemCountSpecifiedByClient, cancellationToken);
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

                    QueryDefinition includeQuery = _queryBuilder.BuildSqlQuerySpec(includeSearchOptions, Array.Empty<IncludeExpression>());

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
                            matches.Select(m =>
                                Expression.And(
                                    Expression.Equals(FieldName.ReferenceResourceType, null, m.ResourceTypeName),
                                    Expression.Equals(FieldName.ReferenceResourceId, null, m.ResourceId))).ToList()));

                    Expression expression = Expression.And(sourceTypeExpression, referenceExpression);

                    var revIncludeSearchOptions = new SearchOptions
                    {
                        Expression = expression,
                        MaxItemCount = maxIncludeCount - includes.Count,
                        Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(),
                    };

                    QueryDefinition revIncludeQuery = _queryBuilder.BuildSqlQuerySpec(revIncludeSearchOptions, Array.Empty<IncludeExpression>());

                    (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken) includeResponse = default;
                    do
                    {
                        if (includes.Count >= maxIncludeCount)
                        {
                            includesTruncated = true;
                            break;
                        }

                        includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                            revIncludeQuery,
                            revIncludeSearchOptions,
                            includeResponse.continuationToken,
                            cancellationToken);
                        includes.AddRange(includeResponse.results);
                    }
                    while (!string.IsNullOrEmpty(includeResponse.continuationToken));
                }
            }

            return (includes, includesTruncated);
        }

        private static bool ExtractIncludeExpressions(Expression inputExpression, out Expression expressionWithoutIncludes, out IReadOnlyList<IncludeExpression> includeExpressions, out IReadOnlyList<IncludeExpression> revIncludeExpressions)
        {
            switch (inputExpression)
            {
                case IncludeExpression ie when ie.Reversed:
                    expressionWithoutIncludes = null;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = new[] { ie };
                    return true;
                case IncludeExpression ie:
                    expressionWithoutIncludes = null;
                    includeExpressions = new[] { ie };
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    return true;
                case MultiaryExpression me when me.Expressions.Any(e => e is IncludeExpression):
                    includeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => !ie.Reversed).ToList();
                    revIncludeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => ie.Reversed).ToList();
                    expressionWithoutIncludes = (me.Expressions.Count - (includeExpressions.Count + revIncludeExpressions.Count)) switch
                    {
                        0 => null,
                        1 => me.Expressions.Single(e => !(e is IncludeExpression)),
                        _ => new MultiaryExpression(me.MultiaryOperation, me.Expressions.Where(e => !(e is IncludeExpression)).ToList()),
                    };
                    return true;
                default:
                    expressionWithoutIncludes = inputExpression;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    return false;
            }
        }
    }
}
