// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        private const string CosmosDataStoreHealthCheckName = "DataStoreHealthCheck";

        private readonly IHealthCheckMetricHandler _metricHandler;
        private readonly ResourceHealthDimensionOptions _resourceHealthDimensionOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthCheckMetricPublisher"/> class.
        /// </summary>
        /// <param name="metricHandler">Handler responsible for emitting health metrics.</param>
        /// <param name="resourceHealthDimensionOptions">The resource health metric dimensions.</param>
        public HealthCheckMetricPublisher(IHealthCheckMetricHandler metricHandler, IOptions<ResourceHealthDimensionOptions> resourceHealthDimensionOptions)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));
            EnsureArg.IsNotNull(resourceHealthDimensionOptions, nameof(resourceHealthDimensionOptions));

            _metricHandler = metricHandler;
            _resourceHealthDimensionOptions = resourceHealthDimensionOptions.Value;
        }

        /// <inheritdoc />
        public void Publish(HealthReport healthReport)
        {
            EnsureArg.IsNotNull(healthReport, nameof(healthReport));

            HealthStatusReason defaultReason = GetDefaultReasonForStatus(healthReport.Status);
            HealthStatusReason reason = healthReport.Status == HealthStatus.Degraded
                ? GetHighestSeverityReasonIgnoringCosmos(healthReport, defaultReason)
                : defaultReason;

            _metricHandler.EmitHealthMetric(new HealthCheckMetricNotification
            {
                OverallStatus = healthReport.Status.ToString(),
                Reason = reason.ToString(),
                ArmGeoLocation = _resourceHealthDimensionOptions?.ArmGeoLocation,
                ArmResourceId = _resourceHealthDimensionOptions?.ArmResourceId,
            });
        }

        private static HealthStatusReason GetHighestSeverityReasonIgnoringCosmos(HealthReport report, HealthStatusReason defaultReason)
        {
            HealthStatusReason highestReason = defaultReason;

            foreach (HealthReportEntry entry in report.Entries
                .Where(x => !string.Equals(x.Key, CosmosDataStoreHealthCheckName, System.StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value))
            {
                if (!entry.Data.TryGetValue("Reason", out object reasonValue))
                {
                    continue;
                }

                if (TryParseReason(reasonValue, out HealthStatusReason parsedReason) && parsedReason > highestReason)
                {
                    highestReason = parsedReason;
                }
            }

            return highestReason;
        }

        private static bool TryParseReason(object value, out HealthStatusReason reason)
        {
            if (value is HealthStatusReason enumReason)
            {
                reason = enumReason;
                return true;
            }

            if (value is string text && System.Enum.TryParse(text, ignoreCase: true, out HealthStatusReason parsedReason))
            {
                reason = parsedReason;
                return true;
            }

            reason = default;
            return false;
        }

        private static HealthStatusReason GetDefaultReasonForStatus(HealthStatus status)
        {
            return status switch
            {
                HealthStatus.Healthy => HealthStatusReason.None,
                HealthStatus.Degraded => HealthStatusReason.ServiceDegraded,
                _ => HealthStatusReason.ServiceUnavailable,
            };
        }
    }
}
