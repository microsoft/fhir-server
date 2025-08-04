using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;

namespace Microsoft.Health.Fhir.SqlServer.Features.Health
{
    public class SqlHealthCheck : IHealthCheck
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache;
        private readonly ILogger<SqlHealthCheck> _logger;

        public SqlHealthCheck(ValueCache<CustomerKeyHealth> customerKeyHealthCache, ILogger<SqlHealthCheck> logger)
        {
            _customerKeyHealthCache = customerKeyHealthCache;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var healthStatus = IsHealthyAsync(cancellationToken).GetAwaiter().GetResult();

            if (healthStatus.Status != HealthStatus.Healthy)
            {
                return Task.FromResult(new HealthCheckResult(healthStatus.Status, healthStatus.Description));
            }

            return Task.FromResult(HealthCheckResult.Healthy("SQL Server is healthy"));
        }

        public async Task<HealthCheckResult> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            // Check Customer Key Health - CMK
            var customerKeyHealth = await IsCustomerManagedKeyHealthyAsync(cancellationToken);
            if (!customerKeyHealth.IsHealthy)
            {
                _logger.LogWarning("Customer managed key is unhealthy. Reason: {Reason}", customerKeyHealth.Reason);
                return HealthCheckResult.Degraded($"Customer managed key is unhealthy. Reason: {customerKeyHealth.Reason}");
            }

            // Add more cases as needed

            return HealthCheckResult.Healthy();
        }

        public async Task<CustomerKeyHealth> IsCustomerManagedKeyHealthyAsync(CancellationToken cancellationToken = default)
        {
            return await _customerKeyHealthCache.GetAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
