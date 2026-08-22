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

        /// <summary>Wait statistics were read for the plan.</summary>
        internal const string WaitStatisticsAvailableStatus = "Available";

        /// <summary>The wait query succeeded but returned no row for the plan.</summary>
        internal const string WaitStatisticsUnavailableStatus = "Unavailable";

        /// <summary>The wait query itself failed, so wait fields are missing because collection is broken.</summary>
        internal const string WaitStatisticsFailedStatus = "Failed";

        private const string QueryStoreStateSql = @"
-- readonly_reason is a bitmask, and it is int here while neighbouring Query Store columns (query_id, plan_id,
-- count_executions, rows, rows_sampled, modification_counter) are bigint. That inconsistency is the trap: reading
-- this column with GetInt64 throws InvalidCastException at runtime, which no compiler or unit test will catch.
SELECT actual_state_desc, readonly_reason
FROM sys.database_query_store_options;";

        private const string SlowQueriesSql = @"
-- Query Store duration and CPU values are recorded in MICROSECONDS while the emitted contract is in MILLISECONDS,
-- which is what every /1000.0 below is for. Query Store also stores PER-INTERVAL averages, so combining intervals
-- requires weighting each interval average by its count_executions first; an unweighted mean across intervals with
-- unequal execution counts is mathematically wrong.
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
-- 'statistics' is a RESERVED T-SQL keyword and cannot be used as a table alias, which is why the rollup CTE is
-- aliased runtimeRollup rather than statistics.
FROM PlanRuntimeStatistics AS runtimeRollup
INNER JOIN sys.query_store_plan AS queryStorePlan
    ON runtimeRollup.plan_id = queryStorePlan.plan_id
INNER JOIN sys.query_store_query AS queryStoreQuery
    ON queryStorePlan.query_id = queryStoreQuery.query_id
INNER JOIN sys.query_store_query_text AS queryText
    ON queryStoreQuery.query_text_id = queryText.query_text_id
WHERE runtimeRollup.execution_count > 0
    AND runtimeRollup.total_duration_microseconds / runtimeRollup.execution_count / 1000.0 >= @MinDurationMilliseconds
    -- Query Store does NOT preserve comments in query_sql_text, so a marker comment cannot be used to identify a
    -- statement. The watchdog therefore excludes its own statements by catalog name, and the integration tests
    -- identify their probe query by a GUID-derived result-column alias, which Query Store does preserve.
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
-- 'statistics' is a RESERVED T-SQL keyword and cannot be used as a table alias, which is why sys.stats is
-- aliased statisticsObject rather than statistics.
FROM sys.stats AS statisticsObject
INNER JOIN sys.objects AS queryObject
    ON statisticsObject.object_id = queryObject.object_id
INNER JOIN sys.tables AS queryTable
    ON queryObject.object_id = queryTable.object_id
LEFT JOIN sys.indexes AS queryIndex
    ON statisticsObject.object_id = queryIndex.object_id
        AND statisticsObject.stats_id = queryIndex.index_id
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

            // By the time this hook runs, the base class has already overwritten PeriodSec with the value stored in
            // dbo.Parameters. That store is write-once in practice: the seeding INSERT is a silent no-op on a database
            // that already holds the row, because dbo.Parameters has IGNORE_DUP_KEY = ON. So a deployment that changes
            // the configured period on an existing database gets no effect and no error. Surface the divergence rather
            // than leaving it to be inferred from collection timestamps.
            if (PeriodSec != _configuration.PeriodSec)
            {
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: configured PeriodSec is {ConfiguredPeriodSec} but the stored value in dbo.Parameters is {StoredPeriodSec}, which takes precedence and also sets the lookback window. Update the '{PeriodSecId}' row to change it.",
                    _configuration.PeriodSec,
                    PeriodSec,
                    PeriodSecId);
            }
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
                var collectionTime = DateTimeOffset.UtcNow;
                var startTime = collectionTime.AddSeconds(-lookbackPeriodSec);
                var queryStoreState = await GetQueryStoreStateAsync(cancellationToken);
                if (queryStoreState == null)
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: sys.database_query_store_options returned no row, so Query Store is not configured on this database. Skipping collection.");
                    return;
                }

                if (!string.Equals(queryStoreState.ActualState, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: Query Store is unavailable for diagnostics. State={QueryStoreState}, ReadonlyReason={ReadonlyReason}, ReadonlyReasonDescription={ReadonlyReasonDescription}",
                        queryStoreState.ActualState,
                        queryStoreState.ReadonlyReason,
                        DescribeReadonlyReason(queryStoreState.ReadonlyReason));
                    return;
                }

                await CollectDiagnosticsAsync(startTime, collectionTime, cancellationToken);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // This filter covers the whole method body rather than each read, which loses the ability to name the
                // failing statement. That is the accepted trade-off: the watchdog only runs when an operator enabled
                // it in both configuration and dbo.Parameters, so "the views you asked me to read do not exist" is
                // always operator-actionable and permanent, and per-read catches would be more churn than value.
                // Because the filter spans every read, the missing view can be the last one, after slow queries and
                // plans have already been published; the message is therefore deliberately worded to be true of a
                // partial tick as well as of one that emitted nothing.
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: collection was aborted because a required Query Store or statistics view is unavailable. Any diagnostics already emitted during this collection were still published.");
            }
            catch (SqlException ex) when (ex.Number == 229 || ex.Number == 262)
            {
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: SQL permissions do not allow diagnostics collection.");
            }
        }

        /// <summary>
        /// Runs the collection itself, once the configuration, runtime and Query Store state gates have all passed.
        /// Separated from <see cref="RunWorkAsync"/> so that the reads and the notifications they produce are
        /// reachable from unit tests: the gates above read <c>dbo.Parameters</c> through
        /// <see cref="SqlCommandExtensions.ExecuteScalarAsync(SqlCommand, ISqlRetryService, ILogger, CancellationToken, string, bool, bool)"/>,
        /// which materializes its value inside a callback executed against a live <see cref="SqlCommand"/> and so
        /// cannot be substituted. Exception handling deliberately stays in the caller.
        /// </summary>
        /// <param name="startTime">The start of the collection window.</param>
        /// <param name="collectionTime">The end of the collection window.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal async Task CollectDiagnosticsAsync(DateTimeOffset startTime, DateTimeOffset collectionTime, CancellationToken cancellationToken)
        {
            IReadOnlyList<SlowQueryResult> slowQueries = Array.Empty<SlowQueryResult>();
            var waitStatisticsFailed = false;
            if (_configuration.SlowQueryCount <= 0)
            {
                // Skipping the round-trip rather than running it with TOP (0) keeps this degenerate configuration
                // reading the same way as the StatisticsHealthCount one below.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: SlowQueryCount is {SlowQueryCount}, which disables slow-query collection. Configure a positive value to collect slow queries.",
                    _configuration.SlowQueryCount);
            }
            else
            {
                slowQueries = await GetSlowQueriesAsync(startTime, cancellationToken);
                var waitStatistics = await GetWaitStatisticsAsync(startTime, slowQueries, cancellationToken);
                waitStatisticsFailed = waitStatistics.Failed;

                foreach (var slowQuery in slowQueries)
                {
                    waitStatistics.Waits.TryGetValue(slowQuery.PlanId, out var wait);
                    var queryText = slowQuery.QueryText;
                    var queryTextTruncated = queryText.Length > MaxFieldLength;
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
                            WaitStatisticsStatus = GetWaitStatisticsStatus(waitStatistics.Failed, wait),
                            QueryText = queryTextTruncated ? queryText.Substring(0, MaxFieldLength) : queryText,
                            QueryTextTruncated = queryTextTruncated,
                            QueryTextLength = queryText.Length,
                            IntervalStart = slowQuery.IntervalStart,
                            IntervalEnd = slowQuery.IntervalEnd,
                        },
                        cancellationToken);
                }
            }

            var queryPlanCount = 0;
            if (!_configuration.IncludeQueryPlans)
            {
                _logger.LogInformation("QueryStoreDiagnosticsWatchdog: query plan collection is turned off by configuration (IncludeQueryPlans).");
            }
            else if (slowQueries.Count > 0)
            {
                queryPlanCount = await PublishQueryPlansAsync(slowQueries, cancellationToken);
            }

            var statisticsHealthCount = 0;
            if (!_configuration.IncludeStatisticsHealth)
            {
                _logger.LogInformation("QueryStoreDiagnosticsWatchdog: statistics health collection is turned off by configuration (IncludeStatisticsHealth).");
            }
            else if (_configuration.StatisticsHealthCount <= 0)
            {
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: StatisticsHealthCount is {StatisticsHealthCount}, which disables statistics health collection. Configure a positive value to collect statistics health.",
                    _configuration.StatisticsHealthCount);
            }
            else
            {
                statisticsHealthCount = await PublishStatisticsHealthAsync(cancellationToken);
            }

            // A completed tick logs unconditionally, including zero counts: without this, "the watchdog has been
            // dead for three days" and "there were no slow queries" are indistinguishable downstream. QueryPlans
            // counts the plans that actually carried sanitized XML, so it is deliberately lower than SlowQueries
            // whenever Query Store had no plan for a query or sanitization rejected one.
            _logger.LogInformation(
                "QueryStoreDiagnosticsWatchdog completed a collection. WindowStart={WindowStart}, WindowEnd={WindowEnd}, SlowQueries={SlowQueryCount}, QueryPlans={QueryPlanCount}, StatisticsHealth={StatisticsHealthCount}, WaitStatisticsFailed={WaitStatisticsFailed}",
                startTime,
                collectionTime,
                slowQueries.Count,
                queryPlanCount,
                statisticsHealthCount,
                waitStatisticsFailed);
        }

        private async Task<QueryStoreState> GetQueryStoreStateAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(QueryStoreStateSql);

            // Every diagnostics read binds to the primary: isReadOnly would route to a read-only secondary when
            // SupportsSqlReplicas is on, and Query Store state is primary-scoped, so a secondary reports READ_ONLY
            // and the state gate below would silently disable collection forever. Replica routing is also decided
            // per call, so the state check and the data reads could otherwise land on different servers and produce
            // torn results. The cost is negligible: one collection per period, hourly by default.
            var states = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new QueryStoreState(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                _logger,
                "Failed to read Query Store state",
                cancellationToken);

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
                cancellationToken);
        }

        private async Task<(Dictionary<long, WaitStatistics> Waits, bool Failed)> GetWaitStatisticsAsync(
            DateTimeOffset startTime,
            IReadOnlyList<SlowQueryResult> slowQueries,
            CancellationToken cancellationToken)
        {
            if (slowQueries.Count == 0)
            {
                return (new Dictionary<long, WaitStatistics>(), false);
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
                    cancellationToken);

                return (waits.ToDictionary(wait => wait.PlanId), false);
            }
            catch (SqlException ex)
            {
                // SqlException is caught broadly on purpose: a transient wait-query failure must never abort the tick
                // and suppress the runtime metrics, which are the primary signal. The cost of that breadth is that
                // timeouts, deadlocks, permission denials and missing views all look alike here, so the failure is
                // logged as a warning and surfaced on every notification as WaitStatisticsStatus = Failed.
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: Query Store wait statistics could not be read for this collection. Wait fields will be empty.");
                return (new Dictionary<long, WaitStatistics>(), true);
            }
        }

        private async Task<int> PublishQueryPlansAsync(IReadOnlyList<SlowQueryResult> slowQueries, CancellationToken cancellationToken)
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
                cancellationToken);
            var plansById = plans.ToDictionary(plan => plan.PlanId);
            var publishedPlanCount = 0;

            foreach (var slowQuery in slowQueries)
            {
                plansById.TryGetValue(slowQuery.PlanId, out var queryPlan);
                var sanitizedPlan = QueryPlanSanitizer.Sanitize(queryPlan?.QueryPlan, MaxFieldLength);
                if (!string.Equals(sanitizedPlan.Status, QueryPlanSanitizer.SanitizedStatus, StringComparison.Ordinal))
                {
                    // Without this, systematic sanitizer breakage looks exactly like "plans are simply unavailable"
                    // unless a downstream handler happens to surface SanitizationStatus.
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: query plan was not emitted because sanitization did not succeed. PlanId={PlanId}, SanitizationStatus={SanitizationStatus}",
                        slowQuery.PlanId,
                        sanitizedPlan.Status);
                }

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

                // A notification is published for every slow query, including the ones with no usable plan, but only
                // the ones that carried XML are counted: a count that always equalled the slow-query count would tell
                // an operator nothing about whether plans are actually arriving.
                if (sanitizedPlan.Xml != null)
                {
                    publishedPlanCount++;
                }
            }

            return publishedPlanCount;
        }

        private async Task<int> PublishStatisticsHealthAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(StatisticsHealthSql);
            command.Parameters.Add("@Top", SqlDbType.Int).Value = _configuration.StatisticsHealthCount;

            // The reader projects straight into the notification contract: an intermediate DTO here would be a
            // property-for-property copy of it and nothing else.
            var statisticsHealth = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new StatisticsHealthNotification
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
                cancellationToken);

            foreach (var statistic in statisticsHealth)
            {
                await _mediator.PublishAsync(statistic, cancellationToken);
            }

            return statisticsHealth.Count;
        }

        private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(IsEnabledId, cancellationToken);
            return value == 1;
        }

        /// <summary>
        /// Decodes the <c>sys.database_query_store_options.readonly_reason</c> bitmask into a readable reason list,
        /// so an operator sees the cause rather than an integer to look up. Exposed as internal only for unit testing.
        /// </summary>
        /// <param name="readonlyReason">The bitmask value, or null when no value was reported.</param>
        /// <returns>A comma-separated description of the set bits.</returns>
        internal static string DescribeReadonlyReason(int? readonlyReason)
        {
            // Every bit this method knows how to name. An unrecognized bit is reported rather than dropped, so a
            // state flag introduced by a newer SQL Server reaches the operator instead of vanishing behind whichever
            // documented bits happened to be set alongside it.
            const int knownReasonMask = 1 | 2 | 4 | 8 | 65536 | 131072;

            if (readonlyReason == null)
            {
                return "not reported";
            }

            if (readonlyReason.Value == 0)
            {
                return "none";
            }

            var reasons = new List<string>();
            if ((readonlyReason.Value & 1) != 0)
            {
                reasons.Add("database is in read-only mode");
            }

            if ((readonlyReason.Value & 2) != 0)
            {
                reasons.Add("database is in single-user mode");
            }

            if ((readonlyReason.Value & 4) != 0)
            {
                reasons.Add("database is in emergency mode");
            }

            if ((readonlyReason.Value & 8) != 0)
            {
                reasons.Add("database is a secondary replica");
            }

            if ((readonlyReason.Value & 65536) != 0)
            {
                reasons.Add("Query Store has reached its size limit (MAX_STORAGE_SIZE_MB)");
            }

            if ((readonlyReason.Value & 131072) != 0)
            {
                reasons.Add("Query Store has reached the limit on the number of statements");
            }

            if (reasons.Count == 0)
            {
                return "unrecognized reason";
            }

            var unrecognizedBits = readonlyReason.Value & ~knownReasonMask;
            if (unrecognizedBits != 0)
            {
                reasons.Add(FormattableString.Invariant($"unrecognized reason bits {unrecognizedBits} (readonly_reason = {readonlyReason.Value})"));
            }

            return string.Join(", ", reasons);
        }

        private static string GetWaitStatisticsStatus(bool waitStatisticsFailed, WaitStatistics wait)
        {
            if (waitStatisticsFailed)
            {
                return WaitStatisticsFailedStatus;
            }

            return wait == null ? WaitStatisticsUnavailableStatus : WaitStatisticsAvailableStatus;
        }

        internal sealed class SlowQueryResult
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

        private sealed class QueryStoreState
        {
            // Trap: sys.database_query_store_options.readonly_reason is int, NOT bigint, even though the neighbouring
            // Query Store columns this watchdog reads (query_id, plan_id, SUM(count_executions), rows, rows_sampled,
            // modification_counter) genuinely are bigint. Reading it with GetInt64 compiles and only fails at runtime
            // with InvalidCastException, so it must be read with GetInt32 and held as int.
            internal QueryStoreState(string actualState, int? readonlyReason)
            {
                ActualState = actualState;
                ReadonlyReason = readonlyReason;
            }

            internal string ActualState { get; }

            internal int? ReadonlyReason { get; }
        }

        internal sealed class WaitStatistics
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
    }
}
