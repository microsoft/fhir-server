// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterStatusDataStore
    {
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null);

        Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken);

        void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses);

        Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken);

        /// <summary>
        /// Checks whether all active instances have converged their search parameter caches
        /// to at least the specified target timestamp.
        /// </summary>
        /// <param name="targetSearchParamLastUpdated">The target SearchParamLastUpdated timestamp to check for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="CacheConsistencyResult"/> indicating convergence status.</returns>
        Task<CacheConsistencyResult> CheckCacheConsistencyAsync(string targetSearchParamLastUpdated, CancellationToken cancellationToken);
    }
}
