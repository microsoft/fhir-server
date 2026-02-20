// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Stats;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Stats
{
    /// <summary>
    /// Interface for providing resource statistics from the data store.
    /// </summary>
    public interface IStatsProvider
    {
        /// <summary>
        /// Gets resource statistics from the data store.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="StatsResponse"/> containing resource statistics.</returns>
        Task<StatsResponse> GetStatsAsync(CancellationToken cancellationToken);
    }
}
