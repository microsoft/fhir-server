// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class SqlDatabaseMetricsWatchdog : Watchdog<SqlDatabaseMetricsWatchdog>
    {
        private const int DefaultPeriodSec = 60;
        private const int DefaultLeasePeriodSec = 120;

        private readonly ILogger<SqlDatabaseMetricsWatchdog> _logger;
        private readonly ISqlDatabaseResourceStatsReader _reader;
        private readonly ISqlDatabaseResourceMetricHandler _metricHandler;
        private readonly SqlMetricsWatchdogConfiguration _configuration;

        public SqlDatabaseMetricsWatchdog(
            ISqlRetryService sqlRetryService,
            ISqlDatabaseResourceStatsReader reader,
            ISqlDatabaseResourceMetricHandler metricHandler,
            IOptions<WatchdogConfiguration> watchdogConfiguration,
            ILogger<SqlDatabaseMetricsWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _reader = EnsureArg.IsNotNull(reader, nameof(reader));
            _metricHandler = EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(watchdogConfiguration?.Value?.SqlMetrics, nameof(watchdogConfiguration));

            PeriodSec = _configuration.PeriodSeconds;

            _logger.LogInformation(
                "SqlDatabaseMetricsWatchdog configured with Enabled={Enabled}, PeriodSeconds={PeriodSeconds}.",
                _configuration.Enabled,
                PeriodSec);
        }

        internal SqlDatabaseMetricsWatchdog()
            : base()
        {
        }

        public override double LeasePeriodSec { get; internal set; } = DefaultLeasePeriodSec;

        public override bool AllowRebalance { get; internal set; } = false;

        public override double PeriodSec { get; internal set; } = DefaultPeriodSec;

        internal Task RunWorkForTestingAsync(CancellationToken cancellationToken) => RunWorkAsync(cancellationToken);

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.Enabled)
            {
                _logger.LogDebug("SqlDatabaseMetricsWatchdog is disabled. Skipping metric emission.");
                return;
            }

            try
            {
                SqlDatabaseResourceStats stats = await _reader.GetLatestAsync(cancellationToken);
                if (stats == null)
                {
                    _logger.LogDebug("SqlDatabaseMetricsWatchdog: No SQL database resource statistics were returned.");
                    return;
                }

                _metricHandler.Emit(new SqlDatabaseResourceMetricNotification
                {
                    EndTime = stats.EndTime,
                    CpuPercent = stats.CpuPercent,
                    DataIoPercent = stats.DataIoPercent,
                    LogIoPercent = stats.LogIoPercent,
                    MemoryPercent = stats.MemoryPercent,
                    WorkersPercent = stats.WorkersPercent,
                    SessionsPercent = stats.SessionsPercent,
                    InstanceCpuPercent = stats.InstanceCpuPercent,
                    InstanceMemoryPercent = stats.InstanceMemoryPercent,
                });

                _logger.LogDebug(
                    "SqlDatabaseMetricsWatchdog emitted sample at {SampleTime}. Cpu={CpuPercent}, DataIo={DataIoPercent}, LogIo={LogIoPercent}, Workers={WorkersPercent}, Sessions={SessionsPercent}.",
                    stats.EndTime,
                    stats.CpuPercent,
                    stats.DataIoPercent,
                    stats.LogIoPercent,
                    stats.WorkersPercent,
                    stats.SessionsPercent);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "SqlDatabaseMetricsWatchdog failed to emit SQL database resource metrics.");
            }
        }
    }
}
