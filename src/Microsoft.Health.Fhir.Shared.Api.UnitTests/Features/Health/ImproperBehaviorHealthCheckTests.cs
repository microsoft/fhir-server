// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Health
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ImproperBehaviorHealthCheckTests
    {
        private readonly ImproperBehaviorHealthCheck _healthCheck;

        public ImproperBehaviorHealthCheckTests()
        {
            _healthCheck = new ImproperBehaviorHealthCheck();
        }

        [Theory]
        [InlineData(true, "Healthy")]
        [InlineData(false, "Unhealthy")]
        public async Task GivenNotification_WhenCheckingHealth_ThenCorrectResultShouldBeReturned(
            bool healthy,
            string message)
        {
            if (!healthy)
            {
                var notification = new ImproperBehaviorNotification(message);
                await _healthCheck.HandleAsync(
                    notification,
                    CancellationToken.None);
            }

            var result = await _healthCheck.CheckHealthAsync(
                null,
                CancellationToken.None);

            Assert.Equal(healthy ? HealthStatus.Healthy : HealthStatus.Unhealthy, result.Status);
            if (!healthy)
            {
                Assert.Contains(message, result.Description);
            }
        }
    }
}
