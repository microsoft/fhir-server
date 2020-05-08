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
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class FhirCosmosSearchService : SearchService
    {
        private readonly CosmosFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
        }

        protected override async Task<SearchResult> SearchInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            SearchResult searchResult = await ExecuteSearchAsync(
                _queryBuilder.BuildSqlQuerySpec(searchOptions),
                searchOptions,
                cancellationToken);

            if (searchOptions.IncludeTotal == TotalType.Accurate && !searchOptions.CountOnly)
            {
                // If this is the first page and there aren't any more pages
                if (searchOptions.ContinuationToken == null && searchResult.ContinuationToken == null)
                {
                    // Count the results on the page.
                    searchResult.TotalCount = searchResult.Results.Count();
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        searchOptions.CountOnly = true;

                        // And perform a second read.
                        var totalSearchResult = await ExecuteSearchAsync(
                            _queryBuilder.BuildSqlQuerySpec(searchOptions),
                            searchOptions,
                            cancellationToken);

                        searchResult.TotalCount = totalSearchResult.TotalCount;
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
            return await ExecuteSearchAsync(
                _queryBuilder.GenerateHistorySql(searchOptions),
                searchOptions,
                cancellationToken);
        }

        private async Task<SearchResult> ExecuteSearchAsync(
            SqlQuerySpec sqlQuerySpec,
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            var feedOptions = new FeedOptions
            {
                EnableCrossPartitionQuery = true,
                MaxItemCount = searchOptions.MaxItemCount,
                RequestContinuation = searchOptions.CountOnly ? null : searchOptions.ContinuationToken,
            };

            if (searchOptions.CountOnly)
            {
                return new SearchResult(
                    (await _fhirDataStore.ExecuteDocumentQueryAsync<int>(sqlQuerySpec, feedOptions, cancellationToken)).Single(),
                    searchOptions.UnsupportedSearchParams);
            }

            FeedResponse<Document> fetchedResults = await _fhirDataStore.ExecuteDocumentQueryAsync<Document>(sqlQuerySpec, feedOptions, cancellationToken);

            SearchResultEntry[] wrappers = fetchedResults
                .Select(r => new SearchResultEntry(r.GetPropertyValue<FhirCosmosResourceWrapper>(SearchValueConstants.RootAliasName))).ToArray();

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
                wrappers,
                searchOptions.UnsupportedSearchParams,
                unsupportedSortingParameters,
                fetchedResults.ResponseContinuation);
        }
    }
}
