// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Core.Features.Metric;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Logging.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class HealthCheckMetricPublisherTests
    {
        private const string DataStoreHealthCheck = "DataStoreHealthCheck";
        private const string StorageInitializedHealthCheck = "StorageInitializedHealthCheck";
        private const string BehaviorHealthCheck = "BehaviorHealthCheck";

        [Fact]
        public void GivenHealthyStatus_WhenPublish_ThenMetricIsEmittedWithReportStatusAndDimensions()
        {
            IHealthCheckMetricHandler metricHandler = Substitute.For<IHealthCheckMetricHandler>();
            var publisher = CreatePublisher(metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateEntry(HealthStatus.Healthy) },
                    { StorageInitializedHealthCheck, CreateEntry(HealthStatus.Healthy) },
                    { DataStoreHealthCheck, CreateEntry(HealthStatus.Healthy) },
                },
                HealthStatus.Healthy,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Healthy.ToString() &&
                n.Reason == HealthStatusReason.None.ToString() &&
                n.ArmGeoLocation == "eastus" &&
                n.ArmResourceId == "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir"));
        }

        [Fact]
        public void GivenUnhealthyStatus_WhenPublish_ThenReasonIsServiceUnavailable()
        {
            IHealthCheckMetricHandler metricHandler = Substitute.For<IHealthCheckMetricHandler>();
            var publisher = CreatePublisher(metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateEntry(HealthStatus.Healthy) },
                    { StorageInitializedHealthCheck, CreateEntry(HealthStatus.Unhealthy) },
                    { DataStoreHealthCheck, CreateEntry(HealthStatus.Unhealthy) },
                },
                HealthStatus.Unhealthy,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Unhealthy.ToString() &&
                n.Reason == HealthStatusReason.ServiceUnavailable.ToString()));
        }

        private static HealthCheckMetricPublisher CreatePublisher(IHealthCheckMetricHandler metricHandler)
        {
            return new HealthCheckMetricPublisher(
                metricHandler,
                Options.Create(new ResourceHealthDimensionOptions
                {
                    ArmGeoLocation = "eastus",
                    ArmResourceId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir",
                }));
        }

        private static HealthReportEntry CreateEntry(HealthStatus status)
        {
            return new HealthReportEntry(
                status,
                description: null,
                duration: TimeSpan.FromMilliseconds(1),
                exception: null,
                data: new Dictionary<string, object>(),
                tags: null);
        }
    }
}
