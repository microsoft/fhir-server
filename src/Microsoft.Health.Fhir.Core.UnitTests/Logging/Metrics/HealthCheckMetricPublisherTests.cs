// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
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
        private readonly IHealthCheckMetricHandler _metricHandler;

        private const string DataStoreHealthCheck = "DataStoreHealthCheck";
        private const string StorageInitializedHealthCheck = "StorageInitializedHealthCheck";
        private const string BehaviorHealthCheck = "BehaviorHealthCheck";

        public HealthCheckMetricPublisherTests()
        {
            _metricHandler = Substitute.For<IHealthCheckMetricHandler>();
        }

        [Fact]
        public void GivenReportWithServiceUnavailable_WhenPublish_ThenServiceUnavailableIsPublished()
        {
            var publisher = CreatePublisher(_metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { StorageInitializedHealthCheck, CreateDummyEntry(HealthStatusReason.ServiceUnavailable) },
                    { DataStoreHealthCheck, CreateDummyEntry(HealthStatusReason.ServiceUnavailable) },
                },
                HealthStatus.Unhealthy,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            _metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Unhealthy.ToString() &&
                n.Reason == HealthStatusReason.ServiceUnavailable.ToString() &&
                n.ArmGeoLocation == "eastus" &&
                n.ArmResourceId == "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir"));
        }

        [Fact]
        public void GivenUnhealthyStatusWithNoneReason_WhenPublish_ThenReasonIsServiceUnavailable()
        {
            var publisher = CreatePublisher(_metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { StorageInitializedHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { DataStoreHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                },
                HealthStatus.Unhealthy,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            _metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Unhealthy.ToString() &&
                n.Reason == HealthStatusReason.ServiceUnavailable.ToString() &&
                n.ArmGeoLocation == "eastus" &&
                n.ArmResourceId == "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir"));
        }

        [Fact]
        public void GivenDegradedStatusWithNoneReason_WhenPublish_ThenReasonIsServiceDegraded()
        {
            var publisher = CreatePublisher(_metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { StorageInitializedHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { DataStoreHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                },
                HealthStatus.Degraded,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            _metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Degraded.ToString() &&
                n.Reason == HealthStatusReason.ServiceDegraded.ToString() &&
                n.ArmGeoLocation == "eastus" &&
                n.ArmResourceId == "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir"));
        }

        [Fact]
        public void GivenHealthyStatus_WhenPublish_ThenMetricIsEmittedWithReportStatusAndDimensions()
        {
            var publisher = CreatePublisher(_metricHandler);

            var healthReport = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    { BehaviorHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { StorageInitializedHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                    { DataStoreHealthCheck, CreateDummyEntry(HealthStatusReason.None) },
                },
                HealthStatus.Healthy,
                TimeSpan.FromMilliseconds(1));

            publisher.Publish(healthReport);

            _metricHandler.Received(1).EmitHealthMetric(Arg.Is<HealthCheckMetricNotification>(n =>
                n.OverallStatus == HealthStatus.Healthy.ToString() &&
                n.Reason == HealthStatusReason.None.ToString() &&
                n.ArmGeoLocation == "eastus" &&
                n.ArmResourceId == "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir"));
        }

        private static HealthCheckMetricPublisher CreatePublisher(IHealthCheckMetricHandler metricHandler)
        {
            return new HealthCheckMetricPublisher(
                metricHandler,
                Substitute.For<ILogger<HealthCheckMetricPublisher>>(),
                Options.Create(new ResourceHealthDimensionOptions
                {
                    ArmGeoLocation = "eastus",
                    ArmResourceId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.HealthcareApis/services/fhir",
                }));
        }

        private static HealthReportEntry CreateDummyEntry(HealthStatusReason reason)
        {
            return new HealthReportEntry(
                HealthStatus.Healthy,
                description: "Some description",
                duration: TimeSpan.FromMilliseconds(1),
                exception: null,
                data: new Dictionary<string, object>() { { "Reason", reason } },
                tags: null);
        }
    }
}
