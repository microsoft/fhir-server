// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid.Util;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.Api.Features.Health;

namespace Microsoft.Health.Fhir.SqlServer.Features.Health
{
    /// <summary>
    /// SQL implementation of <see cref="IDatabaseStatusReporter"/> using a ValueCache of CustomerKeyHealth.
    /// </summary>
    public class SqlStatusReporter : IDatabaseStatusReporter
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache;

        public SqlStatusReporter(ValueCache<CustomerKeyHealth> customerKeyHealthCache)
        {
            _customerKeyHealthCache = EnsureArg.IsNotNull(customerKeyHealthCache, nameof(customerKeyHealthCache));
        }

        public async Task<HealthCheckResult> IsCustomerManagerKeyProperlySetAsync(CancellationToken cancellationToken = default)
        {
            // Check Customer-Managed Key Health - CMK
            CustomerKeyHealth customerKeyHealth = await IsCustomerManagedKeyHealthyAsync(cancellationToken);

            if (!customerKeyHealth.IsHealthy)
            {
                // If the customer-managed key is inaccessible, storage will also be inaccessible
                // When customer-managed key is unhealthy, we return a degraded health check result as it is a customer error.
                return new HealthCheckResult(
                    HealthStatus.Degraded,
                    SqlStatusReporterConstants.DegradedDescription,
                    customerKeyHealth.Exception,
                    new Dictionary<string, object> { { "Reason", customerKeyHealth.Reason.ToString() } });
            }

            // If no specific issues, return healthy status
            return HealthCheckResult.Healthy();
        }

        private async Task<CustomerKeyHealth> IsCustomerManagedKeyHealthyAsync(CancellationToken cancellationToken = default)
        {
            return await _customerKeyHealthCache.GetAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
