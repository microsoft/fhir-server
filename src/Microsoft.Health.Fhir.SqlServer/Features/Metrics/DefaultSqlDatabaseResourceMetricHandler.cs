// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    internal sealed class DefaultSqlDatabaseResourceMetricHandler : BaseMeterMetricHandler, ISqlDatabaseResourceMetricHandler
    {
        private readonly Histogram<double> _cpuPercent;
        private readonly Histogram<double> _dataIoPercent;
        private readonly Histogram<double> _logIoPercent;
        private readonly Histogram<double> _memoryPercent;
        private readonly Histogram<double> _workersPercent;
        private readonly Histogram<double> _sessionsPercent;
        private readonly Histogram<double> _instanceCpuPercent;
        private readonly Histogram<double> _instanceMemoryPercent;

        public DefaultSqlDatabaseResourceMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _cpuPercent = MetricMeter.CreateHistogram<double>("Sql.Database.CpuPercent", unit: "%");
            _dataIoPercent = MetricMeter.CreateHistogram<double>("Sql.Database.DataIoPercent", unit: "%");
            _logIoPercent = MetricMeter.CreateHistogram<double>("Sql.Database.LogIoPercent", unit: "%");
            _memoryPercent = MetricMeter.CreateHistogram<double>("Sql.Database.MemoryPercent", unit: "%");
            _workersPercent = MetricMeter.CreateHistogram<double>("Sql.Database.WorkersPercent", unit: "%");
            _sessionsPercent = MetricMeter.CreateHistogram<double>("Sql.Database.SessionsPercent", unit: "%");
            _instanceCpuPercent = MetricMeter.CreateHistogram<double>("Sql.Database.InstanceCpuPercent", unit: "%");
            _instanceMemoryPercent = MetricMeter.CreateHistogram<double>("Sql.Database.InstanceMemoryPercent", unit: "%");
        }

        public void Emit(SqlDatabaseResourceMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _cpuPercent.Record(notification.CpuPercent);
            _dataIoPercent.Record(notification.DataIoPercent);
            _logIoPercent.Record(notification.LogIoPercent);
            _memoryPercent.Record(notification.MemoryPercent);
            _workersPercent.Record(notification.WorkersPercent);
            _sessionsPercent.Record(notification.SessionsPercent);

            if (notification.InstanceCpuPercent.HasValue)
            {
                _instanceCpuPercent.Record(notification.InstanceCpuPercent.Value);
            }

            if (notification.InstanceMemoryPercent.HasValue)
            {
                _instanceMemoryPercent.Record(notification.InstanceMemoryPercent.Value);
            }
        }
    }
}
