// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultBundleMetricHandler : BaseSuccessRateMetricHandler, IBundleMetricHandler
    {
        private readonly Histogram<long> _bundleLatencyHistogram;

        public DefaultBundleMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "Bundle.Success", failureMetricName: "Bundle.Failure")
        {
            _bundleLatencyHistogram = MetricMeter.CreateHistogram<long>("Bundle.Latency");
        }

        public void EmitLatency(BundleMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _bundleLatencyHistogram.Record(notification.ElapsedMilliseconds);
        }
    }
}
