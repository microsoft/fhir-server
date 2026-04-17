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
        DateTimeOffset? SearchParamLastUpdated { get; }

        Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        Task ValidateSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken);

        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        Task EnsureNoActiveReindexJobAsync(CancellationToken cancellationToken);

        /// <summary>
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="zeroWaitForSemaphore">Whether to wait for the semaphore to become available.</param>
        /// <returns>A task that returns true if the refresh was performed, false if it was skipped due to exceeding the lock interval.</returns>
        Task<bool> GetAndApplySearchParameterUpdates(CancellationToken cancellationToken, bool zeroWaitForSemaphore = false);

        string GetSearchParameterHash(string resourceType);
    }
}
