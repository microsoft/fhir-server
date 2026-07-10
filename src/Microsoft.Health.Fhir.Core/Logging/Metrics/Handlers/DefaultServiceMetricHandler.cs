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
        /// <summary>
        /// Minimum interval between two availability emissions. The health check endpoint can be polled
        /// frequently (and can fail on every poll), so emissions are throttled to avoid flooding the
        /// metric pipeline with a measurement for every evaluation.
        /// </summary>
        internal static readonly TimeSpan AvailabilityEmissionInterval = TimeSpan.FromMinutes(1);

        private readonly Gauge<long> _availabilityGauge;
        private readonly object _emissionLock = new object();

        private DateTimeOffset _availabilityLastEmission = DateTimeOffset.MinValue;

        public DefaultServiceMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _availabilityGauge = MetricMeter.CreateGauge<long>("Service.Availability");
        }

        public void EmitAvailability(bool isAvailable)
        {
            DateTimeOffset now = Clock.UtcNow;

            lock (_emissionLock)
            {
                if (now - _availabilityLastEmission < AvailabilityEmissionInterval)
                {
                    return;
                }

                _availabilityLastEmission = now;
            }

            _availabilityGauge.Record(isAvailable ? 1 : 0);
        }
    }
}
