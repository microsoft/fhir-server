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
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken);

        Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken);

        void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses);

        /// <summary>
        /// Gets the maximum LastUpdated timestamp from the search parameter status store.
        /// This is used for efficient cache validation without retrieving all records.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Maximum LastUpdated timestamp, or DateTimeOffset.MinValue if no records exist</returns>
        Task<DateTimeOffset> GetMaxLastUpdatedAsync(CancellationToken cancellationToken);
    }
}
