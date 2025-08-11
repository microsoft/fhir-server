// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Threading;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Threading
{
    /// <summary>
    /// SQL Server-aware resource monitor that extends runtime monitoring with database-specific metrics.
    /// </summary>
    public sealed class SqlResourceMonitor : ISqlResourceMonitor
    {
        private readonly RuntimeResourceMonitor _runtimeResourceMonitor;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlResourceMonitor> _logger;
        private readonly Timer _sqlMetricsTimer;
        private readonly object _sqlMetricsLock = new object();

        // SQL metrics cache
        private DateTime _lastSqlMetricsUpdate = DateTime.MinValue;
        private double _cachedConnectionPoolUtilization;
        private bool _cachedHasBlockedProcesses;
        private double _cachedTransactionLogUsage;
        private SqlDeadlockStatistics _cachedDeadlockStats = new SqlDeadlockStatistics();
        private readonly List<SqlWaitStatistic> _cachedWaitStats = new List<SqlWaitStatistic>();

        // Performance counters for SQL monitoring (Windows only)
        private PerformanceCounter _sqlConnectionPoolCounter;
        private PerformanceCounter _sqlDeadlockCounter;

        public SqlResourceMonitor(
            ILogger<SqlResourceMonitor> logger,
            ISqlRetryService sqlRetryService,
            ILoggerFactory loggerFactory = null)
        {
            _logger = logger;
            _sqlRetryService = sqlRetryService;

            // Create a RuntimeResourceMonitor instance for delegation instead of injecting it
            // This avoids circular dependency when SqlResourceMonitor is registered as IRuntimeResourceMonitor
            var runtimeLogger = loggerFactory?.CreateLogger<RuntimeResourceMonitor>() ??
                                Microsoft.Extensions.Logging.Abstractions.NullLogger<RuntimeResourceMonitor>.Instance;
            _runtimeResourceMonitor = new RuntimeResourceMonitor(runtimeLogger);

            // Initialize SQL-specific performance counters on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InitializeWindowsSqlCounters();
            }

            // Update SQL metrics every 30 seconds, but delay initial execution by 10 seconds to allow application startup
            _sqlMetricsTimer = new Timer(UpdateSqlMetricsCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

            _logger.LogInformation("SqlResourceMonitor initialized with SQL Server monitoring capabilities");
        }

        public async Task<double> GetConnectionPoolUtilizationAsync()
        {
            await EnsureSqlMetricsUpdated();
            lock (_sqlMetricsLock)
            {
                return _cachedConnectionPoolUtilization;
            }
        }

        public async Task<IEnumerable<SqlWaitStatistic>> GetTopSqlWaitStatisticsAsync()
        {
            await EnsureSqlMetricsUpdated();
            lock (_sqlMetricsLock)
            {
                return _cachedWaitStats.ToList(); // Return a copy
            }
        }

        public async Task<bool> HasBlockedProcessesAsync()
        {
            await EnsureSqlMetricsUpdated();
            lock (_sqlMetricsLock)
            {
                return _cachedHasBlockedProcesses;
            }
        }

        public async Task<double> GetTransactionLogUsageAsync()
        {
            await EnsureSqlMetricsUpdated();
            lock (_sqlMetricsLock)
            {
                return _cachedTransactionLogUsage;
            }
        }

        public async Task<bool> IsSqlUnderPressureAsync()
        {
            try
            {
                // Combine multiple SQL metrics to determine pressure
                var connectionPoolUtil = await GetConnectionPoolUtilizationAsync();
                var hasBlocked = await HasBlockedProcessesAsync();
                var logUsage = await GetTransactionLogUsageAsync();
                var waitStats = await GetTopSqlWaitStatisticsAsync();

                // SQL is under pressure if:
                // 1. Connection pool utilization > 80%
                // 2. There are blocked processes
                // 3. Transaction log usage > 85%
                // 4. High wait times on critical resources
                var highWaitTimes = waitStats.Any(w => (w.WaitType.Contains("PAGEIOLATCH", StringComparison.OrdinalIgnoreCase) || w.WaitType.Contains("WRITELOG", StringComparison.OrdinalIgnoreCase) || w.WaitType.Contains("LCK_", StringComparison.OrdinalIgnoreCase)) && w.WaitTimeMs > 5000); // > 5 seconds total wait time

                var underPressure = connectionPoolUtil > 80 || hasBlocked || logUsage > 85 || highWaitTimes;

                _logger.LogDebug(
                    "SQL pressure check: ConnectionPool={ConnectionPool:F1}%, Blocked={HasBlocked}, LogUsage={LogUsage:F1}%, HighWaits={HighWaits}, UnderPressure={UnderPressure}",
                    connectionPoolUtil,
                    hasBlocked,
                    logUsage,
                    highWaitTimes,
                    underPressure);

                return underPressure;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to determine SQL pressure, assuming not under pressure");
                return false;
            }
        }

        public async Task<int> GetActiveConnectionCountAsync()
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(*) 
                    FROM sys.dm_exec_sessions 
                    WHERE is_user_process = 1 AND status = 'running'")
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 5,
                };

                var results = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => reader.GetInt32(0),
                    _logger,
                    "Getting active connection count",
                    CancellationToken.None,
                    isReadOnly: true);

                return results.Count > 0 ? results[0] : 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get active connection count");
                return 0;
            }
        }

        public async Task<SqlDeadlockStatistics> GetDeadlockStatisticsAsync()
        {
            await EnsureSqlMetricsUpdated();
            lock (_sqlMetricsLock)
            {
                return new SqlDeadlockStatistics
                {
                    DeadlocksPerSecond = _cachedDeadlockStats.DeadlocksPerSecond,
                    TotalDeadlockCount = _cachedDeadlockStats.TotalDeadlockCount,
                    LastDeadlockTime = _cachedDeadlockStats.LastDeadlockTime,
                };
            }
        }

        // IRuntimeResourceMonitor implementation - delegate to the base monitor
        public int GetCurrentProcessorCount() => _runtimeResourceMonitor.GetCurrentProcessorCount();

        public long GetCurrentAvailableMemoryMB() => _runtimeResourceMonitor.GetCurrentAvailableMemoryMB();

        public double GetCurrentMemoryUsagePercentage() => _runtimeResourceMonitor.GetCurrentMemoryUsagePercentage();

        public bool IsUnderResourcePressure() => _runtimeResourceMonitor.IsUnderResourcePressure();

        private void InitializeWindowsSqlCounters()
        {
            try
            {
#pragma warning disable CA1416 // Windows-specific code
                _sqlConnectionPoolCounter = new PerformanceCounter("SQLServer:General Statistics", "User Connections");
                _sqlDeadlockCounter = new PerformanceCounter("SQLServer:Locks", "Number of Deadlocks/sec", "_Total");

                // Prime the counters
                _sqlConnectionPoolCounter.NextValue();
                _sqlDeadlockCounter.NextValue();

                _logger.LogInformation("SQL Server performance counters initialized");
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize SQL Server performance counters, falling back to DMV queries");
                _sqlConnectionPoolCounter?.Dispose();
                _sqlDeadlockCounter?.Dispose();
                _sqlConnectionPoolCounter = null;
                _sqlDeadlockCounter = null;
            }
        }

        private void UpdateSqlMetricsCallback(object state)
        {
            // Fire and forget async call with error handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await UpdateSqlMetricsAsync(state);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to update SQL metrics in timer callback");
                }
            });
        }

        private async Task EnsureSqlMetricsUpdated()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastSqlMetricsUpdate).TotalSeconds > 30)
                {
                    await UpdateSqlMetricsAsync(null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to ensure SQL metrics are updated");
            }
        }

        private async Task UpdateSqlMetricsAsync(object state)
        {
            try
            {
                var tasks = new[]
                {
                    UpdateConnectionPoolMetricsAsync(),
                    UpdateBlockedProcessesAsync(),
                    UpdateTransactionLogUsageAsync(),
                    UpdateWaitStatisticsAsync(),
                    UpdateDeadlockStatisticsAsync(),
                };

                await Task.WhenAll(tasks);

                lock (_sqlMetricsLock)
                {
                    _lastSqlMetricsUpdate = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update SQL metrics");
            }
        }

        private async Task UpdateConnectionPoolMetricsAsync()
        {
            try
            {
                double utilization = 0;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _sqlConnectionPoolCounter != null)
                {
#pragma warning disable CA1416 // Windows-specific code
                    var currentConnections = _sqlConnectionPoolCounter.NextValue();
                    utilization = Math.Min(100, (currentConnections / 100) * 100); // Assume max 100 connections as baseline
#pragma warning restore CA1416
                }
                else
                {
                    // Fallback to DMV query
                    using var cmd = new SqlCommand(@"
                        SELECT COUNT(*) * 100.0 / 
                        (SELECT CAST(value AS int) FROM sys.configurations WHERE name = 'user connections')
                        FROM sys.dm_exec_sessions 
                        WHERE is_user_process = 1")
                    {
                        CommandType = CommandType.Text,
                        CommandTimeout = 5,
                    };

                    var results = await _sqlRetryService.ExecuteReaderAsync(
                        cmd,
                        reader => reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0),
                        _logger,
                        "Getting connection pool utilization",
                        CancellationToken.None,
                        isReadOnly: true);

                    utilization = results.Count > 0 ? results[0] : 0.0;
                }

                lock (_sqlMetricsLock)
                {
                    _cachedConnectionPoolUtilization = Math.Max(0, Math.Min(100, utilization));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update connection pool metrics");
            }
        }

        private async Task UpdateBlockedProcessesAsync()
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(*) 
                    FROM sys.dm_exec_requests 
                    WHERE blocking_session_id > 0")
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 5,
                };

                var results = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => reader.GetInt32(0),
                    _logger,
                    "Getting blocked processes count",
                    CancellationToken.None,
                    isReadOnly: true);

                var blockedCount = results.Count > 0 ? results[0] : 0;

                lock (_sqlMetricsLock)
                {
                    _cachedHasBlockedProcesses = blockedCount > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update blocked processes metrics");
            }
        }

        private async Task UpdateTransactionLogUsageAsync()
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT 
                        CAST(FILEPROPERTY(name, 'SpaceUsed') AS float) / 
                        CAST(size AS float) * 100 as log_usage_pct
                    FROM sys.database_files 
                    WHERE type_desc = 'LOG'")
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 5,
                };

                var results = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0),
                    _logger,
                    "Getting transaction log usage",
                    CancellationToken.None,
                    isReadOnly: true);

                var logUsage = results.Count > 0 ? results[0] : 0.0;

                lock (_sqlMetricsLock)
                {
                    _cachedTransactionLogUsage = Math.Max(0, Math.Min(100, logUsage));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update transaction log usage metrics");
            }
        }

        private async Task UpdateWaitStatisticsAsync()
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    SELECT TOP 10 
                        wait_type,
                        waiting_tasks_count,
                        wait_time_ms,
                        max_wait_time_ms,
                        signal_wait_time_ms
                    FROM sys.dm_os_wait_stats 
                    WHERE wait_type NOT IN (
                        'CLR_SEMAPHORE', 'LAZYWRITER_SLEEP', 'RESOURCE_QUEUE', 
                        'SLEEP_TASK', 'SLEEP_SYSTEMTASK', 'SQLTRACE_BUFFER_FLUSH',
                        'WAITFOR', 'LOGMGR_QUEUE', 'CHECKPOINT_QUEUE', 'REQUEST_FOR_DEADLOCK_SEARCH'
                    )
                    AND wait_time_ms > 100
                    ORDER BY wait_time_ms DESC")
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 10,
                };

                var waitStats = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => new SqlWaitStatistic
                    {
                        WaitType = reader.GetString("wait_type"),
                        WaitingTasksCount = reader.GetInt64("waiting_tasks_count"),
                        WaitTimeMs = reader.GetInt64("wait_time_ms"),
                        MaxWaitTimeMs = reader.GetInt64("max_wait_time_ms"),
                        SignalWaitTimeMs = reader.GetInt64("signal_wait_time_ms"),
                    },
                    _logger,
                    "Getting wait statistics",
                    CancellationToken.None,
                    isReadOnly: true);

                lock (_sqlMetricsLock)
                {
                    _cachedWaitStats.Clear();
                    _cachedWaitStats.AddRange(waitStats);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update wait statistics");
            }
        }

        private async Task UpdateDeadlockStatisticsAsync()
        {
            try
            {
                double deadlocksPerSecond = 0;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _sqlDeadlockCounter != null)
                {
#pragma warning disable CA1416 // Windows-specific code
                    deadlocksPerSecond = _sqlDeadlockCounter.NextValue();
#pragma warning restore CA1416
                }

                // Query for deadlock count from event log or ring buffer
                using var cmd = new SqlCommand(@"
                    SELECT COUNT(*) 
                    FROM sys.dm_os_performance_counters 
                    WHERE counter_name = 'Number of Deadlocks/sec' 
                    AND object_name LIKE '%Locks%'")
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 5,
                };

                var results = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => reader.IsDBNull(0) ? 0L : (long)reader.GetInt32(0),
                    _logger,
                    "Getting deadlock statistics",
                    CancellationToken.None,
                    isReadOnly: true);

                var totalDeadlocks = results.Count > 0 ? results[0] : 0L;

                lock (_sqlMetricsLock)
                {
                    _cachedDeadlockStats.DeadlocksPerSecond = deadlocksPerSecond;
                    _cachedDeadlockStats.TotalDeadlockCount = totalDeadlocks;
                    _cachedDeadlockStats.LastDeadlockTime = totalDeadlocks > 0 ? DateTime.UtcNow : null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update deadlock statistics");
            }
        }

        public void Dispose()
        {
            _sqlMetricsTimer?.Dispose();
            _sqlConnectionPoolCounter?.Dispose();
            _sqlDeadlockCounter?.Dispose();
            _runtimeResourceMonitor?.Dispose();
        }
    }
}
