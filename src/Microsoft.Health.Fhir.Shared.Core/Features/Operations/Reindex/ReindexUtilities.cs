﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexUtilities : IReindexUtilities
    {
        private Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private ISearchIndexer _searchIndexer;
        private ResourceDeserializer _deserializer;
        private readonly ISupportedSearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;

        public ReindexUtilities(
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            ISearchIndexer searchIndexer,
            ResourceDeserializer deserializer,
            ISupportedSearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterRegistry searchParameterRegistry)
        {
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));

            _fhirDataStoreFactory = fhirDataStoreFactory;
            _searchIndexer = searchIndexer;
            _deserializer = deserializer;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterRegistry = searchParameterRegistry;
        }

        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if an update is needed
        /// Needed updates will be committed in a batch
        /// </summary>
        /// <param name="results">The resource batch to process</param>
        /// <param name="searchParamHash">the current hash value of the search parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A Task</returns>
        public async Task ProcessSearchResultsAsync(SearchResult results, string searchParamHash, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(results, nameof(results));
            EnsureArg.IsNotNull(searchParamHash, nameof(searchParamHash));

            var updateHashValueOnly = new List<ResourceWrapper>();
            var updateSearchIndices = new List<ResourceWrapper>();

            foreach (var entry in results.Results)
            {
                entry.Resource.SearchParameterHash = searchParamHash;
                var resourceElement = _deserializer.Deserialize(entry.Resource);
                var newIndices = _searchIndexer.Extract(resourceElement);

                if (entry.Resource.SearchIndicesEqual(newIndices))
                {
                    updateHashValueOnly.Add(entry.Resource);
                }
                else
                {
                    entry.Resource.UpdateSearchIndices(newIndices);
                    updateSearchIndices.Add(entry.Resource);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory())
            {
                await store.Value.UpdateSearchParameterHashBatchAsync(updateHashValueOnly, cancellationToken);
                await store.Value.UpdateSearchParameterIndicesBatchAsync(updateSearchIndices, cancellationToken);
            }
        }

        public async Task<(bool, string)> UpdateSearchParameters(IReadOnlyCollection<string> searchParameterUris, CancellationToken cancellationToken)
        {
            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();

            foreach (string uri in searchParameterUris)
            {
                var searchParamUri = new Uri(uri);

                try
                {
                    _searchParameterDefinitionManager.SetSearchParameterEnabled(searchParamUri);
                }
                catch (SearchParameterNotSupportedException)
                {
                    return (false, string.Format(Core.Resources.SearchParameterNoLongerSupported, uri));
                }

                searchParameterStatusList.Add(new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTimeOffset.UtcNow,
                    Status = SearchParameterStatus.Enabled,
                    Uri = searchParamUri,
                });
            }

            await _searchParameterRegistry.UpdateStatuses(searchParameterStatusList);

            return (true, null);
        }
    }
}
