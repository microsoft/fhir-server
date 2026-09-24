// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.Metrics;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultServiceMetricHandler : BaseMeterMetricHandler, IServiceMetricHandler
    {
        private static readonly TimeSpan AvailabilityEmissionInterval = TimeSpan.FromSeconds(30);

        private readonly Counter<int> _availabilityUptimeCounter;
        private readonly Counter<int> _availabilityDowntimeCounter;
        private readonly object _emissionLock;

        private DateTimeOffset _availabilityLastEmission = DateTimeOffset.MinValue;

        public DefaultServiceMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _availabilityUptimeCounter = MetricMeter.CreateCounter<int>("Service.Availability.Uptime");
            _availabilityDowntimeCounter = MetricMeter.CreateCounter<int>("Service.Availability.Downtime");
            _emissionLock = new object();
        }

        public void ReportAvailabilityUptime()
        {
            EmitAvailabilityMetrics(() => { _availabilityUptimeCounter.Add(1); });
        }

        public void ReportAvailabilityDowntime()
        {
            EmitAvailabilityMetrics(() => { _availabilityDowntimeCounter.Add(1); });
        }

        private void EmitAvailabilityMetrics(Action reportAvailability)
        {
            DateTimeOffset now = Clock.UtcNow;

            lock (_emissionLock)
            {
                if (now - _availabilityLastEmission < AvailabilityEmissionInterval)
                {
                    return;
                }

                reportAvailability();

                _availabilityLastEmission = now;
            }
        }
    }
}
