// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexUtilities : IReindexUtilities
    {
        private Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;

        public ReindexUtilities(
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            SearchParameterStatusManager searchParameterStatusManager,
            IResourceWrapperFactory resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));

            _fhirDataStoreFactory = fhirDataStoreFactory;
            _searchParameterStatusManager = searchParameterStatusManager;
            _resourceWrapperFactory = resourceWrapperFactory;
        }

        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if an update is needed
        /// Needed updates will be committed in a batch
        /// </summary>
        /// <param name="results">The resource batch to process</param>
        /// <param name="resourceTypeSearchParameterHashMap">Map of resource type to current hash value of the search parameters for that resource type</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A Task</returns>
        public async Task ProcessSearchResultsAsync(SearchResult results, IReadOnlyDictionary<string, string> resourceTypeSearchParameterHashMap, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(results, nameof(results));
            EnsureArg.IsNotNull(resourceTypeSearchParameterHashMap, nameof(resourceTypeSearchParameterHashMap));

            var updateSearchIndices = new List<ResourceWrapper>();

            foreach (var entry in results.Results)
            {
                if (!resourceTypeSearchParameterHashMap.TryGetValue(entry.Resource.ResourceTypeName, out string searchParamHash))
                {
                    searchParamHash = string.Empty;
                }

                entry.Resource.SearchParameterHash = searchParamHash;
                _resourceWrapperFactory.Update(entry.Resource);
                updateSearchIndices.Add(entry.Resource);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory())
            {
                await store.Value.BulkUpdateSearchParameterIndicesAsync(updateSearchIndices, cancellationToken);
            }
        }
    }
}
