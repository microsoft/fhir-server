// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterOperations
    {
        DateTimeOffset SearchParamLastUpdated { get; }

        Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false, bool isHardDelete = false);

        Task<DateTimeOffset> ValidateSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken, DateTimeOffset? lastUpdated = null);

        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        /// <summary>
        /// This method should be called to get any updates to search param cache
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="zeroWaitForSemaphore">Whether to wait for the semaphore to become available.</param>
        /// <returns>A task that returns true if the refresh was performed, false if it was skipped due to exceeding the lock interval.</returns>
        Task<bool> GetAndApplySearchParameterUpdates(CancellationToken cancellationToken, bool zeroWaitForSemaphore = false);

        string GetSearchParameterHash(string resourceType);

        /// <summary>
        /// Deletes the search parameter resource from the data store.
        /// Should only be called after reindex completes and the search parameter is no longer needed.
        /// </summary>
        /// <param name="searchParameterUrl">URL of the search parameter to delete</param>
        /// <param name="hardDelete">True to hard delete, false to soft delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task DeleteSearchParameterResourceAsync(string searchParameterUrl, bool hardDelete, CancellationToken cancellationToken);

        /// <summary>
        /// Gets search parameters by their URLs as typed elements.
        /// Tries direct search first, falls back to full scan for deleted resources.
        /// The ID can be extracted from the typed element using GetStringScalar("id").
        /// </summary>
        /// <param name="searchParameterUrls">URLs of the search parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary mapping URLs to typed elements</returns>
        Task<Dictionary<string, ITypedElement>> GetSearchParametersByUrlsAsync(IReadOnlyCollection<string> searchParameterUrls, CancellationToken cancellationToken);
    }
}
