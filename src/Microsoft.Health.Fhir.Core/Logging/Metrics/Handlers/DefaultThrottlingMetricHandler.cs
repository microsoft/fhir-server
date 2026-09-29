// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultThrottlingMetricHandler : BaseMeterMetricHandler, IThrottlingMetricHandler
    {
        private readonly Gauge<long> _requestsInFlightGauge;
        private readonly Gauge<long> _peakRequestsInFlightGauge;
        private readonly Gauge<long> _queuedRequestsGauge;
        private readonly Gauge<long> _concurrentRequestLimitGauge;

        public DefaultThrottlingMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _requestsInFlightGauge = MetricMeter.CreateGauge<long>("Throttling.RequestsInFlight");
            _peakRequestsInFlightGauge = MetricMeter.CreateGauge<long>("Throttling.PeakRequestsInFlight");
            _queuedRequestsGauge = MetricMeter.CreateGauge<long>("Throttling.QueuedRequests");
            _concurrentRequestLimitGauge = MetricMeter.CreateGauge<long>("Throttling.ConcurrentRequestLimit");
        }

        public void ReportConcurrency(long requestsInFlight, long peakRequestsInFlight, long queuedRequests, long concurrentRequestLimit)
        {
            _requestsInFlightGauge.Record(requestsInFlight);
            _peakRequestsInFlightGauge.Record(peakRequestsInFlight);
            _queuedRequestsGauge.Record(queuedRequests);
            _concurrentRequestLimitGauge.Record(concurrentRequestLimit);
        }
    }
}
