// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Health
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    [Trait("Category", "Unit")]
    public class CosmosStatusReporterTests
    {
        [Fact]
        public async Task GivenHealthyCustomerKeyHealth_WhenIsCustomerManagerKeyProperlySetAsync_ThenReturnsHealthy()
        {
            // Arrange
            var reporter = new CosmosStatusReporter();

            // Act
            HealthCheckResult result = await reporter.IsCustomerManagerKeyProperlySetAsync(CancellationToken.None);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }
}
