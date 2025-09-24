// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterStatusManager
    {
        Task AddSearchParameterStatusAsync(IReadOnlyCollection<string> searchParamUris, CancellationToken cancellationToken);

        Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken);

        Task DeleteSearchParameterStatusAsync(string url, CancellationToken cancellationToken);

        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken);

        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusUpdates(CancellationToken cancellationToken);

        Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken);

        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        /// <summary>
        /// Ensures the search parameter cache is fresh by validating against the database max LastUpdated timestamp.
        /// Uses configurable time-based intervals to balance freshness with performance.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if cache was stale and needs full refresh, false if cache is up to date</returns>
        Task<bool> EnsureCacheFreshnessAsync(CancellationToken cancellationToken = default);
    }
}
