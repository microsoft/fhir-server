// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Health
{
    /// <summary>
    /// Provides a way to check the health status.
    /// </summary>
    public interface IHealthCheck
    {
        /// <summary>
        /// Checks the health status.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of <see cref="HealthCheckResult"/> representing the result.</returns>
        Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
