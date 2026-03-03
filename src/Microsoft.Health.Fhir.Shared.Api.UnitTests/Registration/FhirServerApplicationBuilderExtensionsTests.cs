// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirServerApplicationBuilderExtensionsTests
    {
        [Fact]
        public void GivenAzureTrafficManagerEndpointMonitorUserAgent_WhenIsAzureTrafficManagerEndpointMonitor_ThenReturnsTrue()
        {
            bool isAzureTrafficManagerEndpointMonitor = FhirServerApplicationBuilderExtensions.IsAzureTrafficManagerEndpointMonitor(
                FhirServerApplicationBuilderExtensions.AzureTrafficManagerEndpointMonitorUserAgent);

            Assert.True(isAzureTrafficManagerEndpointMonitor);
        }

        [Fact]
        public void GivenNonAzureTrafficManagerEndpointMonitorUserAgent_WhenIsAzureTrafficManagerEndpointMonitor_ThenReturnsFalse()
        {
            bool isAzureTrafficManagerEndpointMonitor = FhirServerApplicationBuilderExtensions.IsAzureTrafficManagerEndpointMonitor("Some Other User Agent");

            Assert.False(isAzureTrafficManagerEndpointMonitor);
        }

        [Fact]
        public void GivenAzureTrafficManagerEndpointMonitorUserAgent_WhenPublishHealthCheckMetricIfApplicable_ThenPublishesWithSameHealthReport()
        {
            var healthCheckMetricPublisher = Substitute.For<IHealthCheckMetricPublisher>();
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["User-Agent"] = FhirServerApplicationBuilderExtensions.AzureTrafficManagerEndpointMonitorUserAgent;
            httpContext.RequestServices = new ServiceCollection()
                .AddSingleton(healthCheckMetricPublisher)
                .BuildServiceProvider();
            var healthReport = new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero);

            FhirServerApplicationBuilderExtensions.PublishHealthCheckMetricIfApplicable(httpContext, healthReport);

            healthCheckMetricPublisher.Received(1).Publish(Arg.Is<HealthReport>(r => ReferenceEquals(r, healthReport)));
        }

        [Fact]
        public void GivenNonAzureTrafficManagerEndpointMonitorUserAgent_WhenPublishHealthCheckMetricIfApplicable_ThenDoesNotPublish()
        {
            var healthCheckMetricPublisher = Substitute.For<IHealthCheckMetricPublisher>();
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["User-Agent"] = "Some Other User Agent";
            httpContext.RequestServices = new ServiceCollection()
                .AddSingleton(healthCheckMetricPublisher)
                .BuildServiceProvider();
            var healthReport = new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero);

            FhirServerApplicationBuilderExtensions.PublishHealthCheckMetricIfApplicable(httpContext, healthReport);

            healthCheckMetricPublisher.DidNotReceiveWithAnyArgs().Publish(default);
        }
    }
}
