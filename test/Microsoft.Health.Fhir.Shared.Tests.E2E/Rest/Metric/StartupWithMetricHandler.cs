// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Metric
{
    [RequiresIsolatedDatabase]
    public class StartupWithMetricHandler : StartupBaseForCustomProviders
    {
        public StartupWithMetricHandler(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Add<MetricHandler>()
                .Singleton()
                .AsService<INotificationHandler<ApiResponseNotification>>()
                .AsService<INotificationHandler<CosmosStorageRequestMetricsNotification>>()
                .AsService<INotificationHandler<ExportTaskMetricsNotification>>();

            services.Add<TestHealthCheckMetricPublisher>()
                .Singleton()
                .AsSelf()
                .AsService<IHealthCheckMetricPublisher>();
        }
    }

    public class TestHealthCheckMetricPublisher : IHealthCheckMetricPublisher
    {
        public bool WasPublished { get; private set; }

        public HealthStatus? LastPublishedStatus { get; private set; }

        public void Reset()
        {
            WasPublished = false;
            LastPublishedStatus = null;
        }

        public void Publish(HealthReport healthReport)
        {
            WasPublished = true;
            LastPublishedStatus = healthReport.Status;
        }
    }
}
