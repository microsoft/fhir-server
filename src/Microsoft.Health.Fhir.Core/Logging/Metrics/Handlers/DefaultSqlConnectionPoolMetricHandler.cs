// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultSqlConnectionPoolMetricHandler : BaseMeterMetricHandler, ISqlConnectionPoolMetricHandler
    {
        private readonly Gauge<double> _connectionPoolGauge;

        public DefaultSqlConnectionPoolMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _connectionPoolGauge = MetricMeter.CreateGauge<double>("Sql.ConnectionPool");
        }

        public void ReportConnectionPoolCounter(string counterName, double value)
        {
            _connectionPoolGauge.Record(value, KeyValuePair.Create<string, object>("Counter", counterName));
        }
    }
}
