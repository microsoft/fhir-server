// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultJobMonitorMetricHandler : BaseMeterMetricHandler, IJobMonitorMetricHandler
    {
        private readonly Gauge<long> _jobAgeGauge;

        private readonly Gauge<long> _jobDepthGauge;

        public DefaultJobMonitorMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _jobAgeGauge = MetricMeter.CreateGauge<long>("Jobs.OldestQueuedAge");
            _jobDepthGauge = MetricMeter.CreateGauge<long>("Jobs.QueueDepth");
        }

        public void RegisterQueueAge(long age)
        {
            _jobAgeGauge.Record(age);
        }

        public void RegisterQueueDepth(long depth)
        {
            _jobAgeGauge.Record(depth);
        }
    }
}
