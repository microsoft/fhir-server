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
        string SearchParamCacheUpdateProcessName { get; }

        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null);

        Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken, long? reindexId = null);

        void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses);

        Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken);

        /// <summary>
        /// Checks whether all active instances have updated their search parameter caches
        /// </summary>
        /// <param name="updateEventsSince">Only cache update records after this time are considered for convergence.</param>
        /// <param name="activeHostsSince">Only active hosts after this time are considered.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="CacheConsistencyResult"/> indicating convergence status.</returns>
        Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken);
    }
}
