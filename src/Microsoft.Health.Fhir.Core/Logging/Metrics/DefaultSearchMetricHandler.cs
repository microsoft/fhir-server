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
        private readonly Counter<long> _searchLatencyCounter;

        public DefaultSearchMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _searchLatencyCounter = MetricMeter.CreateCounter<long>("Search.Latency");
        }

        public void EmitLatency(SearchMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _searchLatencyCounter.Add(notification.ElapsedMilliseconds);
        }
    }
}
