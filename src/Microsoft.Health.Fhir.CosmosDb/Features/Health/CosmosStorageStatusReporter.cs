// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Cosmos DB implementation of <see cref="IStorageHealthCheckStatusReporter"/>.
    /// Always returns healthy status without performing any checks.
    /// </summary>
    public class CosmosStorageStatusReporter : IStorageHealthCheckStatusReporter
    {
        public CosmosStorageStatusReporter()
        {
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            // [WI] to implement: https://microsofthealth.visualstudio.com/Health/_workitems/edit/166817
            // Fake delay to simulate an async operation
            await Task.Delay(10, cancellationToken);

            return HealthCheckResult.Healthy();
        }
    }
}
