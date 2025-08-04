// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.SqlServer.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Health
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlHealthCheckTests
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache = Substitute.For<ValueCache<CustomerKeyHealth>>();
        private readonly ILogger<SqlHealthCheck> _logger = Substitute.For<ILogger<SqlHealthCheck>>();

        private readonly TestSqlHealthCheck _healthCheck;

        public SqlHealthCheckTests()
        {
            _healthCheck = new TestSqlHealthCheck(_customerKeyHealthCache, _logger);
        }

        [Fact]
        public async Task GivenHealthyDependencies_WhenCheckHealthAsync_ThenReturnsHealthyResult()
        {
            // Arrange
            var healthCheckContext = new HealthCheckContext();
            var customerKeyHealth = new CustomerKeyHealth { IsHealthy = true };
            _customerKeyHealthCache.Set(customerKeyHealth);

            // Act
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(healthCheckContext);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task GivenUnhealthyCustomerKeyHealth_WhenCheckHealthAsync_ThenReturnsDegradedResult()
        {
            // Arrange
            var healthCheckContext = new HealthCheckContext();
            var customerKeyHealth = new CustomerKeyHealth
            {
                IsHealthy = false,
                Reason = HealthStatusReason.CustomerManagedKeyAccessLost,
            };
            _customerKeyHealthCache.Set(customerKeyHealth);

            // Act
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(healthCheckContext);

            // Assert
            Assert.Equal(HealthStatus.Degraded, result.Status);
        }
    }
}
