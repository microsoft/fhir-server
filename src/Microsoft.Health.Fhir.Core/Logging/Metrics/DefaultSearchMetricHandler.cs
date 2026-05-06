// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultSearchMetricHandler : BaseMeterMetricHandler, ISearchMetricHandler
    {
        private readonly Histogram<long> _searchLatencyHistogram;

        public DefaultSearchMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _searchLatencyHistogram = MetricMeter.CreateHistogram<long>("Search.Latency");
        }

        public void EmitLatency(SearchMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _searchLatencyHistogram.Record(notification.ElapsedMilliseconds);
        }
    }
}
