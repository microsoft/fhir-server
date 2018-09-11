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
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Continuation;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class CosmosSearchService : SearchService
    {
        private readonly CosmosDataStore _cosmosDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IContinuationTokenCache _continuationTokenCache;

        public CosmosSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            CosmosDataStore cosmosDataStore,
            IQueryBuilder queryBuilder,
            IBundleFactory bundleFactory,
            IContinuationTokenCache continuationTokenCache)
            : base(searchOptionsFactory, bundleFactory, cosmosDataStore)
        {
            EnsureArg.IsNotNull(cosmosDataStore, nameof(cosmosDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));

            _cosmosDataStore = cosmosDataStore;
            _queryBuilder = queryBuilder;
            _continuationTokenCache = continuationTokenCache;
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
                _queryBuilder.GenerateHistorySql(searchOptions.ResourceType, searchOptions),
                searchOptions,
                cancellationToken);
        }

        private async Task<SearchResult> ExecuteSearchAsync(
            SqlQuerySpec sqlQuerySpec,
            SearchOptions searchOptions,
            CancellationToken cancellationToken)
        {
            string ct = null;

            if (!string.IsNullOrEmpty(searchOptions.ContinuationToken))
            {
                ct = await _continuationTokenCache.GetContinuationTokenAsync(searchOptions.ContinuationToken, cancellationToken);
            }

            var feedOptions = new FeedOptions
            {
                EnableCrossPartitionQuery = true,
                MaxItemCount = searchOptions.MaxItemCount,
                RequestContinuation = ct,
            };

            if (searchOptions.CountOnly)
            {
                IDocumentQuery<int> documentCountQuery = _cosmosDataStore.CreateDocumentQuery<int>(sqlQuerySpec, feedOptions);

                using (documentCountQuery)
                {
                    return new SearchResult(Enumerable.Empty<ResourceWrapper>(), null)
                    {
                        TotalCount = (await documentCountQuery.ExecuteNextAsync<int>(cancellationToken)).Single(),
                    };
                }
            }

            IDocumentQuery<Document> documentQuery = _cosmosDataStore.CreateDocumentQuery<Document>(
                sqlQuerySpec,
                feedOptions);

            using (documentQuery)
            {
                Debug.Assert(documentQuery != null, $"The {nameof(documentQuery)} should not be null.");

                // TODO: We should check to see if we can implement the filtering logic in the stored procedure.
                // We will potentially return less documents than asked but that's allowed in the spec. This is the
                // simplest solution to filter out the duplicates for now until we figure out whether filtering is
                // doable or not in stored procedure.
                FeedResponse<Document> fetchedResults = await documentQuery.ExecuteNextAsync<Document>(cancellationToken);

                IEnumerable<CosmosResourceWrapper> wrappers = fetchedResults
                    .Select(r => r.GetPropertyValue<CosmosResourceWrapper>(SearchValueConstants.RootAliasName));

                var results = new Dictionary<string, CosmosResourceWrapper>(StringComparer.Ordinal);

                foreach (CosmosResourceWrapper wrapper in wrappers)
                {
                    if (!results.ContainsKey(wrapper.Id))
                    {
                        results.Add(wrapper.Id, wrapper);
                    }
                }

                string continuationTokenId = null;

                // TODO: Eventually, we will need to take a snapshot of the search and manage the continuation
                // tokens ourselves since there might be multiple continuation token involved depending on
                // the search.
                if (!string.IsNullOrEmpty(fetchedResults.ResponseContinuation))
                {
                    continuationTokenId = await _continuationTokenCache.SaveContinuationTokenAsync(fetchedResults.ResponseContinuation, cancellationToken);
                }

                return new SearchResult(results.Values, continuationTokenId);
            }
        }
    }
}
