// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
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
    [Trait("Category", "Unit")]
    public class SqlStatusReporterTest
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache = new ValueCache<CustomerKeyHealth>();

        public SqlStatusReporterTest()
        {
            _customerKeyHealthCache.Set(new CustomerKeyHealth
            {
                IsHealthy = true,
            });
        }

        [Fact]
        public async Task GivenHealthyCustomerKeyHealth_WhenIsCustomerManagerKeyProperlySetAsync_ThenReturnsTrue()
        {
            // Check SQL storage status reporter
            var reporter = new SqlStatusReporter(_customerKeyHealthCache);

            // Act
            bool result = await reporter.IsCustomerManagerKeyProperlySetAsync(CancellationToken.None);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task GivenUnhealthyCustomerKeyHealth_WhenIsCustomerManagerKeyProperlySetAsync_ThenReturnsFalse()
        {
            // Set Customer-Managed Key as unhealthy
            _customerKeyHealthCache.Set(new CustomerKeyHealth
            {
                IsHealthy = false,
                Reason = HealthStatusReason.CustomerManagedKeyAccessLost,
            });

            // Check SQL storage status reporter
            var reporter = new SqlStatusReporter(_customerKeyHealthCache);

            // Act
            bool result = await reporter.IsCustomerManagerKeyProperlySetAsync(CancellationToken.None);

            // Assert
            Assert.False(result);
        }
    }
}
