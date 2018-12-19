// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class FhirCosmosSearchService : SearchService
    {
        private readonly FhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;

        public FhirCosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            FhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            IBundleFactory bundleFactory)
            : base(searchOptionsFactory, bundleFactory, fhirDataStore)
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
                IDocumentQuery<int> documentCountQuery = _fhirDataStore.CreateDocumentQuery<int>(sqlQuerySpec, feedOptions);

                using (documentCountQuery)
                {
                    return new SearchResult(Enumerable.Empty<ResourceWrapper>(), null)
                    {
                        TotalCount = (await documentCountQuery.ExecuteNextAsync<int>(cancellationToken)).Single(),
                    };
                }
            }

            IDocumentQuery<Document> documentQuery = _fhirDataStore.CreateDocumentQuery<Document>(
                sqlQuerySpec,
                feedOptions);

            using (documentQuery)
            {
                Debug.Assert(documentQuery != null, $"The {nameof(documentQuery)} should not be null.");

                FeedResponse<Document> fetchedResults = await documentQuery.ExecuteNextAsync<Document>(cancellationToken);

                FhirCosmosResourceWrapper[] wrappers = fetchedResults
                    .Select(r => r.GetPropertyValue<FhirCosmosResourceWrapper>(SearchValueConstants.RootAliasName)).ToArray();

                return new SearchResult(wrappers, fetchedResults.ResponseContinuation);
            }
        }
    }
}
