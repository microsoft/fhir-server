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
        private readonly Counter<long> _bundleLatencyCounter;

        public DefaultBundleMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _bundleLatencyCounter = MetricMeter.CreateCounter<long>("Bundle.Latency");
        }

        public void EmitLatency(BundleMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _bundleLatencyCounter.Add(notification.ElapsedMilliseconds);
        }
    }
}
