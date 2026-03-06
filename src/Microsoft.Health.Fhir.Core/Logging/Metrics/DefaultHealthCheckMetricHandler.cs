// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public class DefaultHealthCheckMetricHandler : BaseMeterMetricHandler, IHealthCheckMetricHandler
    {
        private const string MetricName = "HealthChecks.Results";
        private const string OverallStatusTagName = "OverallStatus";
        private const string ReasonTagName = "Reason";
        private const string ArmGeoLocationTagName = "ArmGeoLocation";
        private const string ArmResourceIdTagName = "ArmResourceId";

        private readonly Counter<long> _healthCheckCounter;

        public DefaultHealthCheckMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _healthCheckCounter = MetricMeter.CreateCounter<long>(MetricName);
        }

        public void EmitHealthMetric(HealthCheckMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _healthCheckCounter.Add(
                1,
                KeyValuePair.Create<string, object>(OverallStatusTagName, notification.OverallStatus ?? string.Empty),
                KeyValuePair.Create<string, object>(ReasonTagName, notification.Reason ?? string.Empty),
                KeyValuePair.Create<string, object>(ArmGeoLocationTagName, notification.ArmGeoLocation ?? string.Empty),
                KeyValuePair.Create<string, object>(ArmResourceIdTagName, notification.ArmResourceId ?? string.Empty));
        }
    }
}
