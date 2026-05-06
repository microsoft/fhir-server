// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultBundleMetricHandler : BaseMeterMetricHandler, IBundleMetricHandler
    {
        private readonly Counter<int> _bundleFailureCounter;
        private readonly Counter<int> _bundleSuccessCounter;
        private readonly Histogram<long> _bundleLatencyHistogram;

        public DefaultBundleMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _bundleLatencyHistogram = MetricMeter.CreateHistogram<long>("Bundle.Latency");
            _bundleFailureCounter = MetricMeter.CreateCounter<int>("Bundle.Failure");
            _bundleSuccessCounter = MetricMeter.CreateCounter<int>("Bundle.Success");
        }

        public void EmitFailure()
        {
            _bundleFailureCounter.Add(1);
        }

        public void EmitLatency(BundleMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _bundleLatencyHistogram.Record(notification.ElapsedMilliseconds);
        }

        public void EmitSuccess()
        {
            _bundleSuccessCounter.Add(1);
        }
    }
}
