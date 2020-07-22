// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexUtilities
    {
        private Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;

        public ReindexUtilities(Func<IScoped<IFhirDataStore>> fhirDataStoreFactory)
        {
            _fhirDataStoreFactory = fhirDataStoreFactory;
        }

        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if a change is needed
        /// Needed updates will br processed in a batch
        /// </summary>
        /// <param name="results">The resource batch to process</param>
        /// <param name="searchParamHash">the current hash value of the search parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A Task</returns>
        public async Task ProcessSearchResultsAsync(SearchResult results, string searchParamHash, CancellationToken cancellationToken)
        {
            // TODO: placeholder, will be updated to extract new indices, compare with old values and update indices
            // It should update only the indices and search parameter hash as a bulk operation, and not affect lastUpdated timestamp
            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory())
            {
                foreach (var result in results.Results)
                {
                    result.Resource.SearchParameterHash = searchParamHash;
                    await store.Value.UpsertAsync(result.Resource, WeakETag.FromVersionId(result.Resource.Version), false, true, cancellationToken);
                }
            }
        }
    }
}
