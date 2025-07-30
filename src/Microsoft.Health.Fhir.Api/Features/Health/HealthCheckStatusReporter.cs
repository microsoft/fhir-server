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
    /// Default implementation of <see cref="IHealthCheckStatusReporter"/>.
    /// Checks the health status of the customer-managed key.
    /// </summary>
    public class HealthCheckStatusReporter : IHealthCheckStatusReporter
    {
        private readonly SqlStatusReporter _sqlCustomerManagedKeyStatusReporter;

        public HealthCheckStatusReporter(SqlStatusReporter sqlCustomerManagedKeyStatusReporter)
        {
            _sqlCustomerManagedKeyStatusReporter = sqlCustomerManagedKeyStatusReporter;
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            // TODO: Check both Cosmos  as well
            return await _sqlCustomerManagedKeyStatusReporter.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
