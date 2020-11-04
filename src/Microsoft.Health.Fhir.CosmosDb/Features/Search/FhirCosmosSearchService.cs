// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features;
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
        private readonly CosmosFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _resourceTypeSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
        }

        protected override async Task<SearchResult> SearchInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            ExtractIncludeExpressions(
                searchOptions.Expression,
                out Expression expressionWithoutIncludes,
                out IReadOnlyList<IncludeExpression> includeExpressions,
                out IReadOnlyList<IncludeExpression> revIncludeExpressions);

            searchOptions.Expression = expressionWithoutIncludes;

            if (searchOptions.CountOnly)
            {
                int count = (await ExecuteSearchAsync<int>(
                    _queryBuilder.BuildSqlQuerySpec(searchOptions, Array.Empty<IncludeExpression>()),
                    searchOptions,
                    searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                    cancellationToken)).Single();

                return new SearchResult(count, searchOptions.UnsupportedSearchParams);
            }

            if (includeExpressions.Any(e => e.Iterate) || revIncludeExpressions.Any(e => e.Iterate))
            {
                throw new BadRequestException("_include:iterate and _revinclude:iterate are not supported");
            }

            FeedResponse<FhirCosmosResourceWrapper> matches = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.BuildSqlQuerySpec(searchOptions, includeExpressions),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                cancellationToken);

            var includes = new List<FhirCosmosResourceWrapper>();

            if (includeExpressions.Count > 0 && matches.Count > 0)
            {
                var referencesToInclude = matches.SelectMany(m => m.ReferencesToInclude).Distinct().ToList();

                if (referencesToInclude.Count > 0)
                {
                    var expression = new SearchParameterExpression(
                        new SearchParameterInfo(SearchValueConstants.TypeIdCompositeSearchParameterName),
                        Expression.Or(referencesToInclude.Select(r =>
                            Expression.And(
                                Expression.Equals(FieldName.TokenCode, 0, r.ResourceTypeName),
                                Expression.Equals(FieldName.TokenCode, 1, r.ResourceId))).ToList()));
                    Expression snapshot = searchOptions.Expression;

                    try
                    {
                        searchOptions.Expression = expression;

                        FeedResponse<FhirCosmosResourceWrapper> includeResponse = null;
                        do
                        {
                            includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                                _queryBuilder.BuildSqlQuerySpec(searchOptions, Array.Empty<IncludeExpression>()),
                                searchOptions,
                                includeResponse?.ContinuationToken,
                                cancellationToken);
                            includes.AddRange(includeResponse);
                        }
                        while (!string.IsNullOrEmpty(includeResponse.ContinuationToken));
                    }
                    finally
                    {
                        searchOptions.Expression = snapshot;
                    }
                }
            }

            if (revIncludeExpressions.Count > 0 && matches.Count > 0)
            {
                foreach (IncludeExpression revIncludeExpression in revIncludeExpressions)
                {
                    SearchParameterExpression sourceTypeExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, revIncludeExpression.SourceResourceType));
                    SearchParameterInfo referenceSearchParameter = revIncludeExpression.WildCard ? new SearchParameterInfo(SearchValueConstants.WildcardReferenceSearchParameterName) : revIncludeExpression.ReferenceSearchParameter;
                    SearchParameterExpression referenceExpression = Expression.SearchParameter(
                        referenceSearchParameter,
                        Expression.Or(
                            matches.Select(m =>
                                Expression.And(
                                    Expression.Equals(FieldName.ReferenceResourceType, null, m.ResourceTypeName),
                                    Expression.Equals(FieldName.ReferenceResourceId, null, m.ResourceId))).ToArray()));

                    Expression expression = Expression.And(sourceTypeExpression, referenceExpression);

                    Expression snapshot = searchOptions.Expression;

                    try
                    {
                        searchOptions.Expression = expression;

                        FeedResponse<FhirCosmosResourceWrapper> includeResponse = null;
                        do
                        {
                            includeResponse = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                                _queryBuilder.BuildSqlQuerySpec(searchOptions, Array.Empty<IncludeExpression>()),
                                searchOptions,
                                includeResponse?.ContinuationToken,
                                cancellationToken);
                            includes.AddRange(includeResponse);
                        }
                        while (!string.IsNullOrEmpty(includeResponse.ContinuationToken));
                    }
                    finally
                    {
                        searchOptions.Expression = snapshot;
                    }
                }
            }

            SearchResult searchResult = CreateSearchResult(searchOptions, matches.Select(m => new SearchResultEntry(m, SearchEntryMode.Match)).Concat(includes.Select(i => new SearchResultEntry(i, SearchEntryMode.Include))), matches.ContinuationToken);

            if (searchOptions.IncludeTotal == TotalType.Accurate && !searchOptions.CountOnly)
            {
                // If this is the first page and there aren't any more pages
                if (searchOptions.ContinuationToken == null && matches.ContinuationToken == null)
                {
                    // Count the results on the page.
                    searchResult.TotalCount = matches.Count;
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        searchOptions.CountOnly = true;

                        // And perform a second read.
                        searchResult.TotalCount = await ExecuteCountSearchAsync(
                            _queryBuilder.BuildSqlQuerySpec(searchOptions, Array.Empty<IncludeExpression>()),
                            searchOptions,
                            cancellationToken);
                    }
                    finally
                    {
                        // Ensure search options is set to its original state.
                        searchOptions.CountOnly = false;
                    }
                }
            }

            return searchResult;
        }

        protected override async Task<SearchResult> SearchHistoryInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            FeedResponse<FhirCosmosResourceWrapper> results = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.GenerateHistorySql(searchOptions),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                cancellationToken);

            return CreateSearchResult(searchOptions, results.Select(r => new SearchResultEntry(r)), results.ContinuationToken);
        }

        protected override async Task<SearchResult> SearchForReindexInternalAsync(
            SearchOptions searchOptions,
            string searchParameterHash,
            CancellationToken cancellationToken)
        {
            FeedResponse<FhirCosmosResourceWrapper> results = await ExecuteSearchAsync<FhirCosmosResourceWrapper>(
                _queryBuilder.GenerateReindexSql(searchOptions, searchParameterHash),
                searchOptions,
                searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
                cancellationToken);

            return CreateSearchResult(searchOptions, results.Select(r => new SearchResultEntry(r)), results.ContinuationToken);
        }

        private async Task<FeedResponse<T>> ExecuteSearchAsync<T>(
            QueryDefinition sqlQuerySpec,
            SearchOptions searchOptions,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            var feedOptions = new QueryRequestOptions
            {
                MaxItemCount = searchOptions.MaxItemCount,
            };

            return await _fhirDataStore.ExecuteDocumentQueryAsync<T>(sqlQuerySpec, feedOptions, continuationToken, cancellationToken);
        }

        private async Task<int> ExecuteCountSearchAsync(
            QueryDefinition sqlQuerySpec,
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            var feedOptions = new QueryRequestOptions
            {
                MaxItemCount = searchOptions.MaxItemCount,
            };

            return (await _fhirDataStore.ExecuteDocumentQueryAsync<int>(sqlQuerySpec, feedOptions, null, cancellationToken)).Single();
        }

        public static SearchResult CreateSearchResult(SearchOptions searchOptions, IEnumerable<SearchResultEntry> results, string continuationToken)
        {
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters;
            if (searchOptions.Sort?.Count > 0)
            {
                unsupportedSortingParameters = searchOptions
                    .UnsupportedSortingParams
                    .Concat(searchOptions.Sort
                        .Where(x => !string.Equals(x.searchParameterInfo.Name, KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        .Select(s => (s.searchParameterInfo.Name, Core.Resources.SortNotSupported))).ToList();
            }
            else
            {
                unsupportedSortingParameters = searchOptions.UnsupportedSortingParams;
            }

            return new SearchResult(
                results,
                searchOptions.UnsupportedSearchParams,
                unsupportedSortingParameters,
                continuationToken);
        }

        private static void ExtractIncludeExpressions(Expression inputExpression, out Expression expressionWithoutIncludes, out IReadOnlyList<IncludeExpression> includeExpressions, out IReadOnlyList<IncludeExpression> revIncludeExpressions)
        {
            switch (inputExpression)
            {
                case IncludeExpression ie when ie.Reversed:
                    expressionWithoutIncludes = null;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = new[] { ie };
                    return;
                case IncludeExpression ie:
                    expressionWithoutIncludes = null;
                    includeExpressions = new[] { ie };
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    return;
                case MultiaryExpression me when me.Expressions.Any(e => e is IncludeExpression):
                    includeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => !ie.Reversed).ToList();
                    revIncludeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => ie.Reversed).ToList();
                    expressionWithoutIncludes = (me.Expressions.Count - (includeExpressions.Count + revIncludeExpressions.Count)) switch
                    {
                        0 => null,
                        1 => me.Expressions.Single(e => !(e is IncludeExpression)),
                        _ => new MultiaryExpression(me.MultiaryOperation, me.Expressions.Where(e => !(e is IncludeExpression)).ToList()),
                    };
                    return;
                default:
                    expressionWithoutIncludes = inputExpression;
                    includeExpressions = Array.Empty<IncludeExpression>();
                    revIncludeExpressions = Array.Empty<IncludeExpression>();
                    return;
            }
        }
    }
}
