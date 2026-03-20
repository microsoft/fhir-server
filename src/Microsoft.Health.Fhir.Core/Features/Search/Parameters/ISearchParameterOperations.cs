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
        /// <param name="forceFullRefresh">When true, forces a full refresh from database instead of incremental updates</param>
        /// <returns>A task.</returns>
        Task GetAndApplySearchParameterUpdates(CancellationToken cancellationToken, bool forceFullRefresh = false);

        string GetSearchParameterHash(string resourceType);

        /// <summary>
        /// Waits for the specified number of successful cache refresh cycles to complete.
        /// Each cycle corresponds to a successful execution of the background cache refresh service.
        /// </summary>
        /// <param name="cycleCount">The number of successful refresh cycles to wait for. If zero or negative, returns immediately.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the requested number of refresh cycles have occurred.</returns>
        Task WaitForRefreshCyclesAsync(int cycleCount, CancellationToken cancellationToken);
    }
}
