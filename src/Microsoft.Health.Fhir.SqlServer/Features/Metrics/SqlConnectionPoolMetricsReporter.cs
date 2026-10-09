// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    /// <summary>
    /// Periodically reports SqlClient connection pool counters, plus the configured per-pool maximum size, to <see cref="ISqlConnectionPoolMetricHandler"/>.
    /// </summary>
    public sealed class SqlConnectionPoolMetricsReporter : IHostedService, IDisposable
    {
        internal const int DefaultIntervalSeconds = 10;

        internal const string ActiveSoftConnectsCounter = "active-soft-connects";

        internal const string MaxPoolSizeCounter = "max-pool-size";

        internal static readonly IReadOnlySet<string> ReportedCounters = new HashSet<string>(StringComparer.Ordinal)
        {
            "active-hard-connections",
            ActiveSoftConnectsCounter,
            "number-of-active-connections",
            "number-of-free-connections",
            "number-of-pooled-connections",
            "number-of-stasis-connections",
            "number-of-active-connection-pools",
            "number-of-reclaimed-connections",
            "hard-connects",
            "soft-connects",
        };

        private readonly ISqlConnectionPoolMetricHandler _metricHandler;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly ILogger<SqlConnectionPoolMetricsReporter> _logger;
        private readonly int _intervalSeconds;
        private readonly object _sync = new object();
        private SqlClientEventCounterListener _listener;
        private int? _maxPoolSize;

        public SqlConnectionPoolMetricsReporter(
            ISqlConnectionPoolMetricHandler metricHandler,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            ILogger<SqlConnectionPoolMetricsReporter> logger)
            : this(metricHandler, sqlServerDataStoreConfiguration, logger, DefaultIntervalSeconds)
        {
        }

        internal SqlConnectionPoolMetricsReporter(
            ISqlConnectionPoolMetricHandler metricHandler,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            ILogger<SqlConnectionPoolMetricsReporter> logger,
            int intervalSeconds)
        {
            _metricHandler = EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));
            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _intervalSeconds = EnsureArg.IsGt(intervalSeconds, 0, nameof(intervalSeconds));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_listener == null)
                {
                    _maxPoolSize = GetEffectiveMaxPoolSize();
                    _listener = new SqlClientEventCounterListener(_intervalSeconds, OnCounter);
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            DisposeListener();
            return Task.CompletedTask;
        }

        public void Dispose() => DisposeListener();

        internal void OnCounter(string counterName, double value)
        {
            if (!ReportedCounters.Contains(counterName))
            {
                return;
            }

            try
            {
                _metricHandler.ReportConnectionPoolCounter(counterName, value);

                // Emit the configured limit once per sampling interval, using a counter SqlClient reports every interval as the trigger. The limit applies
                // to each connection pool separately, while SqlClient counters are totals across all pools (primary, read-only replica, MergeResources, ...),
                // so the two are not a utilization ratio.
                if (_maxPoolSize.HasValue && string.Equals(counterName, ActiveSoftConnectsCounter, StringComparison.Ordinal))
                {
                    _metricHandler.ReportConnectionPoolCounter(MaxPoolSizeCounter, _maxPoolSize.Value);
                }
            }
            catch (Exception ex)
            {
                // Metric reporting runs on the event counter timer thread and must never fault it.
                _logger.LogWarning(ex, "Failed to report SQL connection pool counter {CounterName}.", counterName);
            }
        }

        private int? GetEffectiveMaxPoolSize()
        {
            if (_sqlServerDataStoreConfiguration.MaxPoolSize.HasValue)
            {
                return _sqlServerDataStoreConfiguration.MaxPoolSize.Value;
            }

            try
            {
                return new SqlConnectionStringBuilder(_sqlServerDataStoreConfiguration.ConnectionString ?? string.Empty).MaxPoolSize;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Unable to read Max Pool Size from the SQL connection string. The max-pool-size counter will not be reported.");
                return null;
            }
        }

        private void DisposeListener()
        {
            SqlClientEventCounterListener listener;
            lock (_sync)
            {
                listener = _listener;
                _listener = null;
            }

            listener?.Dispose();
        }
    }
}
