// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Default implementation of <see cref="IStorageHealthCheckStatusReporter"/>.
    /// Always returns healthy status without performing any checks.
    /// </summary>
    public class DefaultStorageHealthCheckStatusReporter : IStorageHealthCheckStatusReporter
    {
        public DefaultStorageHealthCheckStatusReporter()
        {
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            // Fake delay to simulate an async operation
            await Task.Delay(10, cancellationToken);

            return HealthCheckResult.Healthy();
        }
    }
}
