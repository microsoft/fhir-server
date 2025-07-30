// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// SQL implementation of <see cref="IHealthCheckStatusReporter"/> using a ValueCache of CustomerKeyHealth.
    /// </summary>
    public class SqlStatusReporter
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache;

        public SqlStatusReporter(ValueCache<CustomerKeyHealth> customerKeyHealthCache)
        {
            _customerKeyHealthCache = customerKeyHealthCache;
        }

        public async Task<HealthCheckResult> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            // Check Customer Key Health - CMK
            var customerKeyHealth = await GetKeyHealthAsync(cancellationToken).ConfigureAwait(false);
            if (!customerKeyHealth.IsHealthy)
            {
                return HealthCheckResult.Degraded($"Customer managed key is unhealthy. Reason: {customerKeyHealth.Reason}");
            }

            // Add more cases as needed

            return HealthCheckResult.Healthy();
        }

        public async Task<CustomerKeyHealth> GetKeyHealthAsync(CancellationToken cancellationToken = default)
        {
            return await _customerKeyHealthCache.GetAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
