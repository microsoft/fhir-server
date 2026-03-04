// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Core.Features.Metric;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    /// <summary>
    /// Default implementation used to emit health check metrics.
    /// </summary>
    public class HealthCheckMetricPublisher : IHealthCheckMetricPublisher
    {
        private static readonly Dictionary<HealthStatus, HealthStatusReason> DefaultStatusToReasonMapping = new Dictionary<HealthStatus, HealthStatusReason>
        {
            { HealthStatus.Healthy, HealthStatusReason.None },
            { HealthStatus.Degraded, HealthStatusReason.ServiceDegraded },
            { HealthStatus.Unhealthy, HealthStatusReason.ServiceUnavailable },
        };

        private readonly IHealthCheckMetricHandler _metricHandler;
        private readonly ILogger<HealthCheckMetricPublisher> _logger;
        private readonly ResourceHealthDimensionOptions _resourceHealthDimensionOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthCheckMetricPublisher"/> class.
        /// </summary>
        /// <param name="metricHandler">Handler responsible for emitting health metrics.</param>
        /// <param name="logger">Logger used to report metric publishing issues.</param>
        /// <param name="resourceHealthDimensionOptions">The resource health metric dimensions.</param>
        public HealthCheckMetricPublisher(IHealthCheckMetricHandler metricHandler, ILogger<HealthCheckMetricPublisher> logger, IOptions<ResourceHealthDimensionOptions> resourceHealthDimensionOptions)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(resourceHealthDimensionOptions, nameof(resourceHealthDimensionOptions));

            _metricHandler = metricHandler;
            _logger = logger;
            _resourceHealthDimensionOptions = resourceHealthDimensionOptions.Value;
        }

        /// <inheritdoc />
        public void Publish(HealthReport healthReport)
        {
            EnsureArg.IsNotNull(healthReport, nameof(healthReport));

            HealthStatusReason defaultReason = GetDefaultReasonForStatus(healthReport);

            switch (healthReport.Status)
            {
                case HealthStatus.Healthy:
                    _metricHandler.EmitHealthMetric(new HealthCheckMetricNotification
                    {
                        OverallStatus = healthReport.Status.ToString(),
                        Reason = defaultReason.ToString(),
                        ArmGeoLocation = _resourceHealthDimensionOptions?.ArmGeoLocation,
                        ArmResourceId = _resourceHealthDimensionOptions?.ArmResourceId,
                    });
                    break;
                case HealthStatus.Degraded:
                    _metricHandler.EmitHealthMetric(new HealthCheckMetricNotification
                    {
                        OverallStatus = healthReport.Status.ToString(),
                        Reason = HealthCheckMetricPublisher.GetHighestSeverityReason(healthReport, defaultReason).ToString(),
                        ArmGeoLocation = _resourceHealthDimensionOptions?.ArmGeoLocation,
                        ArmResourceId = _resourceHealthDimensionOptions?.ArmResourceId,
                    });
                    break;
                case HealthStatus.Unhealthy:
                    _metricHandler.EmitHealthMetric(new HealthCheckMetricNotification
                    {
                        OverallStatus = healthReport.Status.ToString(),
                        Reason = defaultReason.ToString(),
                        ArmGeoLocation = _resourceHealthDimensionOptions?.ArmGeoLocation,
                        ArmResourceId = _resourceHealthDimensionOptions?.ArmResourceId,
                    });
                    break;
            }
        }

        private HealthStatusReason GetDefaultReasonForStatus(HealthReport report)
        {
            if (DefaultStatusToReasonMapping.TryGetValue(report.Status, out HealthStatusReason defaultReason))
            {
                return defaultReason;
            }

            _logger.LogError("No default HealthStatusReason can be found for HealthStatus {HealthStatus}", report.Status);
            return HealthStatusReason.None;
        }

        public static HealthStatusReason GetHighestSeverityReason(HealthReport healthReport, HealthStatusReason defaultReason)
        {
            HealthStatusReason worstReason = defaultReason;

            foreach (var entry in healthReport.Entries)
            {
                if (entry.Value.Data.TryGetValue("Reason", out object reason))
                {
                    HealthStatusReason healthStatusReason = Enum.Parse<HealthStatusReason>(reason.ToString());

                    if (healthStatusReason > worstReason)
                    {
                        worstReason = healthStatusReason;
                    }
                }
            }

            return worstReason;
        }
    }
}
