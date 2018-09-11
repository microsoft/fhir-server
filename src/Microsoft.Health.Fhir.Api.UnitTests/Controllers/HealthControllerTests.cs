// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Features.Health;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class HealthControllerTests
    {
        private readonly IHealthCheck _healthCheck = Substitute.For<IHealthCheck>();

        private readonly HealthController _controller;

        public HealthControllerTests()
        {
            _controller = new HealthController(_healthCheck)
            {
                ControllerContext = new ControllerContext()
                {
                    HttpContext = new DefaultHttpContext(),
                },
            };
        }

        [Fact]
        public async Task GivenHealthCheckReturnsHealthy_WhenCheckingForHealth_ThenOkResultShouldBeReturned()
        {
            _healthCheck.CheckAsync(Arg.Any<CancellationToken>()).Returns(HealthCheckResult.Healthy("Healthy."));

            IActionResult result = await _controller.Check();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GivenHealthCheckReturnsUnhealthy_WhenCheckingForHealth_ThenStatusCodeResultShouldBeReturned()
        {
            _healthCheck.CheckAsync(Arg.Any<CancellationToken>()).Returns(HealthCheckResult.Unhealthy("Unhealthy."));

            IActionResult result = await _controller.Check();

            ObjectResult statusCodeResult = Assert.IsType<ObjectResult>(result);

            Assert.Equal(503, statusCodeResult.StatusCode);
        }
    }
}
