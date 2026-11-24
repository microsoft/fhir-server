// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterStatusManager
    {
        Task ApplySearchParameterStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken);

        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatuses(CancellationToken cancellationToken);

        Task HandleAsync(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken);

        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, long? reindexId = null, DateTimeOffset? lastUpdated = null);

        Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken, string updateProcess = null);

        Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken);

        /// <summary>
        /// Tries logging a cache update proof to the service-local event log.
        /// </summary>
        /// <param name="process">The event process name.</param>
        /// <param name="text">The cache update proof.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task TryLogCacheUpdateProof(string process, string text, CancellationToken cancellationToken);
    }
}
