// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class QueryStoreDiagnosticsWatchdog : Watchdog<QueryStoreDiagnosticsWatchdog>
    {
        internal const int MaxFieldLength = 32 * 1024;

        private const string QueryStoreStateSql = @"
SELECT actual_state_desc, readonly_reason
FROM sys.database_query_store_options;";

        private const string SlowQueriesSql = @"
-- Query Store duration and CPU values are recorded in microseconds.
-- Aggregate interval averages with their execution counts before converting to milliseconds.
;WITH PlanRuntimeStatistics AS
(
    SELECT
        runtimeStatistics.plan_id,
        SUM(runtimeStatistics.count_executions) AS execution_count,
        SUM(CONVERT(float, runtimeStatistics.avg_duration) * runtimeStatistics.count_executions) AS total_duration_microseconds,
        MAX(CONVERT(float, runtimeStatistics.max_duration)) AS max_duration_microseconds,
        SUM(CONVERT(float, runtimeStatistics.avg_cpu_time) * runtimeStatistics.count_executions) AS total_cpu_microseconds,
        SUM(CONVERT(float, runtimeStatistics.avg_logical_io_reads) * runtimeStatistics.count_executions) AS total_logical_reads,
        MIN(runtimeStatisticsInterval.start_time) AS interval_start,
        MAX(runtimeStatisticsInterval.end_time) AS interval_end
    FROM sys.query_store_runtime_stats AS runtimeStatistics
    INNER JOIN sys.query_store_runtime_stats_interval AS runtimeStatisticsInterval
        ON runtimeStatistics.runtime_stats_interval_id = runtimeStatisticsInterval.runtime_stats_interval_id
    WHERE runtimeStatisticsInterval.end_time >= @StartTime
        AND runtimeStatistics.execution_type = 0 -- regular completed executions only
    GROUP BY runtimeStatistics.plan_id
)
SELECT TOP (@Top)
    queryStoreQuery.query_id,
    queryStorePlan.plan_id,
    runtimeRollup.execution_count,
    runtimeRollup.total_duration_microseconds / 1000.0 AS total_duration_milliseconds,
    runtimeRollup.total_duration_microseconds / runtimeRollup.execution_count / 1000.0 AS average_duration_milliseconds,
    runtimeRollup.max_duration_microseconds / 1000.0 AS max_duration_milliseconds,
    runtimeRollup.total_cpu_microseconds / 1000.0 AS total_cpu_milliseconds,
    runtimeRollup.total_cpu_microseconds / runtimeRollup.execution_count / 1000.0 AS average_cpu_milliseconds,
    runtimeRollup.total_logical_reads,
    runtimeRollup.total_logical_reads / runtimeRollup.execution_count AS average_logical_reads,
    queryText.query_sql_text,
    runtimeRollup.interval_start,
    runtimeRollup.interval_end
FROM PlanRuntimeStatistics AS runtimeRollup
INNER JOIN sys.query_store_plan AS queryStorePlan
    ON runtimeRollup.plan_id = queryStorePlan.plan_id
INNER JOIN sys.query_store_query AS queryStoreQuery
    ON queryStorePlan.query_id = queryStoreQuery.query_id
INNER JOIN sys.query_store_query_text AS queryText
    ON queryStoreQuery.query_text_id = queryText.query_text_id
WHERE runtimeRollup.execution_count > 0
    AND runtimeRollup.total_duration_microseconds / runtimeRollup.execution_count / 1000.0 >= @MinDurationMilliseconds
    -- SQL Server does not preserve diagnostic marker comments in query_sql_text, so exclude every watchdog statement by catalog name.
    -- The explicit case-insensitive collation also keeps the comparison correct on case-sensitive databases.
    AND queryText.query_sql_text COLLATE Latin1_General_CI_AS NOT LIKE N'%query_store%'
    AND queryText.query_sql_text COLLATE Latin1_General_CI_AS NOT LIKE N'%dm_db_stats_properties%'
ORDER BY
    runtimeRollup.total_duration_microseconds DESC,
    queryStoreQuery.query_id,
    queryStorePlan.plan_id;";

        private const string WaitStatisticsSql = @"
-- Wait statistics are collected independently so unavailable wait capture does not suppress slow-query metrics.
;WITH WaitsByCategory AS
(
    SELECT
        waitStatistics.plan_id,
        waitStatistics.wait_category_desc,
        SUM(CONVERT(float, waitStatistics.total_query_wait_time_ms)) AS total_wait_milliseconds
    FROM sys.query_store_wait_stats AS waitStatistics
    INNER JOIN sys.query_store_runtime_stats_interval AS runtimeStatisticsInterval
        ON waitStatistics.runtime_stats_interval_id = runtimeStatisticsInterval.runtime_stats_interval_id
    WHERE runtimeStatisticsInterval.end_time >= @StartTime
        AND waitStatistics.execution_type = 0 -- regular completed executions only
        AND waitStatistics.plan_id IN
        (
            SELECT CONVERT(bigint, [value])
            FROM STRING_SPLIT(@PlanIds, ',')
        )
    GROUP BY waitStatistics.plan_id, waitStatistics.wait_category_desc
),
RankedWaits AS
(
    SELECT
        plan_id,
        SUM(total_wait_milliseconds) OVER (PARTITION BY plan_id) AS total_wait_milliseconds,
        wait_category_desc,
        ROW_NUMBER() OVER
        (
            PARTITION BY plan_id
            ORDER BY total_wait_milliseconds DESC, wait_category_desc
        ) AS wait_category_rank
    FROM WaitsByCategory
)
SELECT plan_id, total_wait_milliseconds, wait_category_desc
FROM RankedWaits
WHERE wait_category_rank = 1;";

        private const string QueryPlansSql = @"
SELECT plan_id, query_plan
FROM sys.query_store_plan
WHERE plan_id IN
(
    SELECT CONVERT(bigint, [value])
    FROM STRING_SPLIT(@PlanIds, ',')
);";

        private const string StatisticsHealthSql = @"
SELECT TOP (@Top)
    SCHEMA_NAME(queryObject.schema_id) AS schema_name,
    queryObject.name AS table_name,
    statisticsObject.name AS statistics_name,
    TODATETIMEOFFSET(statisticsProperties.last_updated, '+00:00') AS last_updated,
    statisticsProperties.rows,
    statisticsProperties.rows_sampled,
    statisticsProperties.modification_counter,
    CASE
        WHEN statisticsProperties.rows IS NULL OR statisticsProperties.rows = 0 THEN NULL
        ELSE CONVERT(float, statisticsProperties.modification_counter) * 100.0 / statisticsProperties.rows
    END AS modification_percent,
    statisticsObject.auto_created,
    statisticsObject.user_created,
    CONVERT(bit, CASE WHEN queryIndex.index_id IS NULL THEN 0 ELSE 1 END) AS is_from_index,
    statisticsObject.has_filter
FROM sys.stats AS statisticsObject
INNER JOIN sys.objects AS queryObject
    ON statisticsObject.object_id = queryObject.object_id
INNER JOIN sys.tables AS queryTable
    ON queryObject.object_id = queryTable.object_id
LEFT JOIN sys.indexes AS queryIndex
    ON statisticsObject.object_id = queryIndex.object_id
        AND statisticsObject.stats_id = queryIndex.index_id
-- OUTER APPLY retains statistics when dm_db_stats_properties has no row.
OUTER APPLY sys.dm_db_stats_properties(statisticsObject.object_id, statisticsObject.stats_id) AS statisticsProperties
WHERE queryObject.is_ms_shipped = 0
    AND queryObject.type = 'U'
    AND queryTable.temporal_type <> 1 -- exclude temporal history tables
ORDER BY
    CASE
        WHEN statisticsProperties.rows IS NULL OR statisticsProperties.rows = 0 THEN NULL
        ELSE CONVERT(float, statisticsProperties.modification_counter) / statisticsProperties.rows
    END DESC,
    statisticsProperties.modification_counter DESC,
    SCHEMA_NAME(queryObject.schema_id),
    queryObject.name,
    statisticsObject.name;";

        private readonly QueryStoreDiagnosticsConfiguration _configuration;
        private readonly ILogger<QueryStoreDiagnosticsWatchdog> _logger;
        private readonly IMediator _mediator;
        private readonly ISqlRetryService _sqlRetryService;

        public QueryStoreDiagnosticsWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<QueryStoreDiagnosticsWatchdog> logger,
            IMediator mediator,
            IOptions<WatchdogConfiguration> watchdogConfiguration)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _configuration = EnsureArg.IsNotNull(watchdogConfiguration?.Value, nameof(watchdogConfiguration)).QueryStoreDiagnostics;
            PeriodSec = _configuration.PeriodSec;
        }

        internal QueryStoreDiagnosticsWatchdog()
            : base()
        {
            // this is used to get param names for testing
        }

        internal string IsEnabledId => $"{Name}.IsEnabled";

        // Ten minutes allows the lease to recover promptly without expiring during a diagnostics collection.
        public override double LeasePeriodSec { get; internal set; } = 600;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 3600;

        /// <summary>
        /// Exposes RunWorkAsync for unit testing purposes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal Task RunWorkForTestingAsync(CancellationToken cancellationToken) => RunWorkAsync(cancellationToken);

        protected override async Task InitAdditionalParamsAsync()
        {
            await using var command = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id, Number) SELECT @IsEnabledId, 0");
            command.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
            await command.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_configuration.Enabled)
                {
                    _logger.LogInformation("QueryStoreDiagnosticsWatchdog is disabled by configuration. Exiting...");
                    return;
                }

                if (!await IsEnabledAsync(cancellationToken))
                {
                    _logger.LogInformation("QueryStoreDiagnosticsWatchdog is not enabled. Exiting...");
                    return;
                }

                var lookbackPeriodSec = Math.Clamp(await GetNumberParameterByIdAsync(PeriodSecId, cancellationToken), 60d, 86400d);
                var startTime = DateTimeOffset.UtcNow.AddSeconds(-lookbackPeriodSec);
                var queryStoreState = await GetQueryStoreStateAsync(cancellationToken);
                if (queryStoreState == null || !string.Equals(queryStoreState.ActualState, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: Query Store is unavailable for diagnostics. State={QueryStoreState}, ReadonlyReason={ReadonlyReason}",
                        queryStoreState?.ActualState ?? "unavailable",
                        queryStoreState?.ReadonlyReason);
                    return;
                }

                var slowQueries = await GetSlowQueriesAsync(startTime, cancellationToken);
                var waitStatistics = await GetWaitStatisticsAsync(startTime, slowQueries, cancellationToken);

                foreach (var slowQuery in slowQueries)
                {
                    waitStatistics.TryGetValue(slowQuery.PlanId, out var wait);
                    var queryText = Truncate(slowQuery.QueryText);
                    await _mediator.PublishAsync(
                        new SlowQueryNotification
                        {
                            QueryId = slowQuery.QueryId,
                            PlanId = slowQuery.PlanId,
                            ExecutionCount = slowQuery.ExecutionCount,
                            TotalDurationMilliseconds = slowQuery.TotalDurationMilliseconds,
                            AverageDurationMilliseconds = slowQuery.AverageDurationMilliseconds,
                            MaxDurationMilliseconds = slowQuery.MaxDurationMilliseconds,
                            TotalCpuMilliseconds = slowQuery.TotalCpuMilliseconds,
                            AverageCpuMilliseconds = slowQuery.AverageCpuMilliseconds,
                            TotalLogicalReads = slowQuery.TotalLogicalReads,
                            AverageLogicalReads = slowQuery.AverageLogicalReads,
                            TotalWaitMilliseconds = wait?.TotalWaitMilliseconds,
                            AverageWaitMilliseconds = wait == null ? null : wait.TotalWaitMilliseconds / slowQuery.ExecutionCount,
                            TopWaitCategory = wait?.TopWaitCategory,
                            QueryText = queryText.Value,
                            QueryTextTruncated = queryText.Truncated,
                            QueryTextLength = queryText.OriginalLength,
                            IntervalStart = slowQuery.IntervalStart,
                            IntervalEnd = slowQuery.IntervalEnd,
                        },
                        cancellationToken);
                }

                if (_configuration.IncludeQueryPlans && slowQueries.Count > 0)
                {
                    await PublishQueryPlansAsync(slowQueries, cancellationToken);
                }

                if (_configuration.IncludeStatisticsHealth && _configuration.StatisticsHealthCount > 0)
                {
                    await PublishStatisticsHealthAsync(cancellationToken);
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogDebug(ex, "QueryStoreDiagnosticsWatchdog: Query Store diagnostics views are unavailable.");
            }
            catch (SqlException ex) when (ex.Number == 229 || ex.Number == 262)
            {
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: SQL permissions do not allow diagnostics collection.");
            }
        }

        private async Task<QueryStoreState> GetQueryStoreStateAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(QueryStoreStateSql);
            var states = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new QueryStoreState(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                _logger,
                "Failed to read Query Store state",
                cancellationToken,
                isReadOnly: true);

            return states.Count == 0 ? null : states[0];
        }

        private async Task<IReadOnlyList<SlowQueryResult>> GetSlowQueriesAsync(DateTimeOffset startTime, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(SlowQueriesSql);
            command.Parameters.Add("@StartTime", SqlDbType.DateTimeOffset).Value = startTime;
            command.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Max(0, _configuration.SlowQueryCount);
            command.Parameters.Add("@MinDurationMilliseconds", SqlDbType.Int).Value = Math.Max(0, _configuration.MinDurationMilliseconds);

            return await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new SlowQueryResult
                {
                    QueryId = reader.GetInt64(0),
                    PlanId = reader.GetInt64(1),
                    ExecutionCount = reader.GetInt64(2),
                    TotalDurationMilliseconds = reader.GetDouble(3),
                    AverageDurationMilliseconds = reader.GetDouble(4),
                    MaxDurationMilliseconds = reader.GetDouble(5),
                    TotalCpuMilliseconds = reader.GetDouble(6),
                    AverageCpuMilliseconds = reader.GetDouble(7),
                    TotalLogicalReads = reader.GetDouble(8),
                    AverageLogicalReads = reader.GetDouble(9),
                    QueryText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    IntervalStart = reader.GetDateTimeOffset(11),
                    IntervalEnd = reader.GetDateTimeOffset(12),
                },
                _logger,
                "Failed to read Query Store slow queries",
                cancellationToken,
                isReadOnly: true);
        }

        private async Task<Dictionary<long, WaitStatistics>> GetWaitStatisticsAsync(
            DateTimeOffset startTime,
            IReadOnlyList<SlowQueryResult> slowQueries,
            CancellationToken cancellationToken)
        {
            if (slowQueries.Count == 0)
            {
                return new Dictionary<long, WaitStatistics>();
            }

            try
            {
                await using var command = new SqlCommand(WaitStatisticsSql);
                command.Parameters.Add("@StartTime", SqlDbType.DateTimeOffset).Value = startTime;
                command.Parameters.Add("@PlanIds", SqlDbType.NVarChar, -1).Value = string.Join(',', slowQueries.Select(query => query.PlanId));

                var waits = await _sqlRetryService.ExecuteReaderAsync(
                    command,
                    reader => new WaitStatistics(
                        reader.GetInt64(0),
                        reader.GetDouble(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)),
                    _logger,
                    "Failed to read Query Store wait statistics",
                    cancellationToken,
                    isReadOnly: true);

                return waits.ToDictionary(wait => wait.PlanId);
            }
            catch (SqlException ex)
            {
                _logger.LogDebug(ex, "QueryStoreDiagnosticsWatchdog: Query Store wait statistics are unavailable for this collection.");
                return new Dictionary<long, WaitStatistics>();
            }
        }

        private async Task PublishQueryPlansAsync(IReadOnlyList<SlowQueryResult> slowQueries, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(QueryPlansSql);
            command.Parameters.Add("@PlanIds", SqlDbType.NVarChar, -1).Value = string.Join(',', slowQueries.Select(query => query.PlanId));

            var plans = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new QueryPlanResult(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1)),
                _logger,
                "Failed to read Query Store plans",
                cancellationToken,
                isReadOnly: true);
            var plansById = plans.ToDictionary(plan => plan.PlanId);

            foreach (var slowQuery in slowQueries)
            {
                plansById.TryGetValue(slowQuery.PlanId, out var queryPlan);
                var sanitizedPlan = QueryPlanSanitizer.Sanitize(queryPlan?.QueryPlan, MaxFieldLength);
                await _mediator.PublishAsync(
                    new QueryPlanNotification
                    {
                        QueryId = slowQuery.QueryId,
                        PlanId = slowQuery.PlanId,
                        SanitizedQueryPlan = sanitizedPlan.Xml,
                        QueryPlanTruncated = sanitizedPlan.Truncated,
                        OriginalQueryPlanLength = sanitizedPlan.OriginalLength,
                        SanitizedQueryPlanLength = sanitizedPlan.SanitizedLength,
                        SanitizationStatus = sanitizedPlan.Status,
                    },
                    cancellationToken);
            }
        }

        private async Task PublishStatisticsHealthAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(StatisticsHealthSql);
            command.Parameters.Add("@Top", SqlDbType.Int).Value = _configuration.StatisticsHealthCount;

            var statisticsHealth = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new StatisticsHealthResult
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    StatisticsName = reader.GetString(2),
                    LastUpdated = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(3),
                    Rows = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
                    RowsSampled = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                    ModificationCounter = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                    ModificationPercent = reader.IsDBNull(7) ? (double?)null : reader.GetDouble(7),
                    IsAutoCreated = reader.GetBoolean(8),
                    IsUserCreated = reader.GetBoolean(9),
                    IsFromIndex = reader.GetBoolean(10),
                    HasFilter = reader.GetBoolean(11),
                },
                _logger,
                "Failed to read statistics health",
                cancellationToken,
                isReadOnly: true);

            foreach (var statistic in statisticsHealth)
            {
                await _mediator.PublishAsync(
                    new StatisticsHealthNotification
                    {
                        SchemaName = statistic.SchemaName,
                        TableName = statistic.TableName,
                        StatisticsName = statistic.StatisticsName,
                        LastUpdated = statistic.LastUpdated,
                        Rows = statistic.Rows,
                        RowsSampled = statistic.RowsSampled,
                        ModificationCounter = statistic.ModificationCounter,
                        ModificationPercent = statistic.ModificationPercent,
                        IsAutoCreated = statistic.IsAutoCreated,
                        IsUserCreated = statistic.IsUserCreated,
                        IsFromIndex = statistic.IsFromIndex,
                        HasFilter = statistic.HasFilter,
                    },
                    cancellationToken);
            }
        }

        private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(IsEnabledId, cancellationToken);
            return value == 1;
        }

        private static TruncatedField Truncate(string value)
        {
            value ??= string.Empty;
            var truncated = value.Length > MaxFieldLength;
            return new TruncatedField(truncated ? value.Substring(0, MaxFieldLength) : value, truncated, value.Length);
        }

        private sealed class SlowQueryResult
        {
            internal long QueryId { get; set; }

            internal long PlanId { get; set; }

            internal long ExecutionCount { get; set; }

            internal double TotalDurationMilliseconds { get; set; }

            internal double AverageDurationMilliseconds { get; set; }

            internal double MaxDurationMilliseconds { get; set; }

            internal double TotalCpuMilliseconds { get; set; }

            internal double AverageCpuMilliseconds { get; set; }

            internal double TotalLogicalReads { get; set; }

            internal double AverageLogicalReads { get; set; }

            internal string QueryText { get; set; }

            internal DateTimeOffset IntervalStart { get; set; }

            internal DateTimeOffset IntervalEnd { get; set; }
        }

        private sealed class StatisticsHealthResult
        {
            internal string SchemaName { get; set; }

            internal string TableName { get; set; }

            internal string StatisticsName { get; set; }

            internal DateTimeOffset? LastUpdated { get; set; }

            internal long? Rows { get; set; }

            internal long? RowsSampled { get; set; }

            internal long? ModificationCounter { get; set; }

            internal double? ModificationPercent { get; set; }

            internal bool IsAutoCreated { get; set; }

            internal bool IsUserCreated { get; set; }

            internal bool IsFromIndex { get; set; }

            internal bool HasFilter { get; set; }
        }

        private sealed class QueryStoreState
        {
            // sys.database_query_store_options.readonly_reason is int, not bigint.
            internal QueryStoreState(string actualState, int? readonlyReason)
            {
                ActualState = actualState;
                ReadonlyReason = readonlyReason;
            }

            internal string ActualState { get; }

            internal int? ReadonlyReason { get; }
        }

        private sealed class WaitStatistics
        {
            internal WaitStatistics(long planId, double totalWaitMilliseconds, string topWaitCategory)
            {
                PlanId = planId;
                TotalWaitMilliseconds = totalWaitMilliseconds;
                TopWaitCategory = topWaitCategory;
            }

            internal long PlanId { get; }

            internal double TotalWaitMilliseconds { get; }

            internal string TopWaitCategory { get; }
        }

        private sealed class QueryPlanResult
        {
            internal QueryPlanResult(long planId, string queryPlan)
            {
                PlanId = planId;
                QueryPlan = queryPlan;
            }

            internal long PlanId { get; }

            internal string QueryPlan { get; }
        }

        private sealed class TruncatedField
        {
            internal TruncatedField(string value, bool truncated, int originalLength)
            {
                Value = value;
                Truncated = truncated;
                OriginalLength = originalLength;
            }

            internal string Value { get; }

            internal bool Truncated { get; }

            internal int OriginalLength { get; }
        }
    }
}
