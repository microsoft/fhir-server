// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.SqlServer.Features.Health;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Health
{
    [Trait("Category", "Unit")]
    public class SqlStorageStatusReporterTest
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache = new ValueCache<CustomerKeyHealth>();

        public SqlStorageStatusReporterTest()
        {
            _customerKeyHealthCache.Set(new CustomerKeyHealth
            {
                IsHealthy = true,
            });
        }

        [Fact]
        public async Task GivenHealthyCustomerKeyHealth_WhenIsHealthyAsync_ThenReturnsHealthy()
        {
            // Check SQL storage status reporter
            var reporter = new SqlStorageStatusReporter(_customerKeyHealthCache);

            // Act
            HealthCheckResult result = await reporter.IsHealthyAsync(CancellationToken.None);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task GivenUnhealthyCustomerKeyHealth_WhenIsHealthyAsync_ThenReturnsDegraded()
        {
            // Set Customer-Managed Key as unhealthy
            _customerKeyHealthCache.Set(new CustomerKeyHealth
            {
                IsHealthy = false,
                Reason = HealthStatusReason.CustomerManagedKeyAccessLost,
            });

            // Check SQL storage status reporter
            var reporter = new SqlStorageStatusReporter(_customerKeyHealthCache);

            // Act
            HealthCheckResult result = await reporter.IsHealthyAsync(CancellationToken.None);

            // Assert
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains(SqlStorageStatusReporterConstants.CustomerManagedKeyUnhealthyMessage, result.Description);
        }
    }
}
