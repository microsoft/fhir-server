// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
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
            IQueryBuilder queryBuilder,
            IModelInfoProvider modelInfoProvider)
            : base(searchOptionsFactory, fhirDataStore, modelInfoProvider)
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
            return await ExecuteSearchAsync(
                _queryBuilder.BuildSqlQuerySpec(searchOptions),
                searchOptions,
                cancellationToken);
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
                RequestContinuation = searchOptions.ContinuationToken,
            };

            if (searchOptions.CountOnly)
            {
                return new SearchResult(
                    (await _fhirDataStore.ExecuteDocumentQueryAsync<int>(sqlQuerySpec, feedOptions, cancellationToken)).Single(),
                    searchOptions.UnsupportedSearchParams);
            }

            FeedResponse<Document> fetchedResults = await _fhirDataStore.ExecuteDocumentQueryAsync<Document>(sqlQuerySpec, feedOptions, cancellationToken);

            FhirCosmosResourceWrapper[] wrappers = fetchedResults
                .Select(r => r.GetPropertyValue<FhirCosmosResourceWrapper>(SearchValueConstants.RootAliasName)).ToArray();

            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters;
            if (searchOptions.Sort?.Count > 0)
            {
                // we don't currently support sort
                unsupportedSortingParameters = searchOptions.UnsupportedSortingParams.Concat(searchOptions.Sort.Select(s => (s.searchParameterInfo.Name, Core.Resources.SortNotSupported))).ToList();
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
