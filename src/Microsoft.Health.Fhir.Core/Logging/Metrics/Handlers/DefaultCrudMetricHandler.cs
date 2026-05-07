// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultCrudMetricHandler : BaseMeterMetricHandler, ICrudMetricHandler
    {
        private readonly Histogram<long> _crudLatencyHistogram;

        public DefaultCrudMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _crudLatencyHistogram = MetricMeter.CreateHistogram<long>("Crud.Latency");
        }

        public void EmitLatency(CrudMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _crudLatencyHistogram.Record(notification.ElapsedMilliseconds);
        }
    }
}
