// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultJobMonitorMetricHandler : BaseMeterMetricHandler, IJobMonitorMetricHandler
    {
        private readonly Gauge<long> _jobAgeGauge;
        private readonly Gauge<long> _jobPendingGauge;
        private readonly Gauge<long> _jobRunningGauge;

        public DefaultJobMonitorMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _jobAgeGauge = MetricMeter.CreateGauge<long>("Jobs.JobQueue.JobsAge");
            _jobPendingGauge = MetricMeter.CreateGauge<long>("Jobs.JobQueue.PendingJobs");
            _jobRunningGauge = MetricMeter.CreateGauge<long>("Jobs.JobQueue.RunningJobs");
        }

        public void ReportJobQueueAge(string queueName, long value)
        {
            _jobAgeGauge.Record(value, KeyValuePair.Create<string, object>("QueueName", queueName));
        }

        public void ReportJobQueuePending(string queueName, long value)
        {
            _jobPendingGauge.Record(value, KeyValuePair.Create<string, object>("QueueName", queueName));
        }

        public void ReportJobQueueRunning(string queueName, long value)
        {
            _jobRunningGauge.Record(value, KeyValuePair.Create<string, object>("QueueName", queueName));
        }
    }
}
