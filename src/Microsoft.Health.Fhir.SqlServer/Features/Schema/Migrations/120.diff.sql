SET XACT_ABORT ON
GO
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsState
AS
BEGIN
    SET NOCOUNT ON;

    SELECT actual_state_desc, readonly_reason
    FROM sys.database_query_store_options;
END
GO
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsSlowQueries
    @StartTime datetimeoffset,
    @Top int,
    @MinDurationMilliseconds int
AS
BEGIN
    SET NOCOUNT ON;

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
            AND runtimeStatistics.execution_type = 0
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
        AND NOT EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsState')),
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsSlowQueries')),
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsWaitStatistics')),
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsPlans')),
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsStatisticsHealth'))
            ) AS diagnosticsProcedures(object_id)
            WHERE diagnosticsProcedures.object_id = queryStoreQuery.object_id
        )
    ORDER BY
        runtimeRollup.total_duration_microseconds DESC,
        queryStoreQuery.query_id,
        queryStorePlan.plan_id;
END
GO
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsWaitStatistics
    @StartTime datetimeoffset,
    @PlanIds nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

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
            AND waitStatistics.execution_type = 0
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
    WHERE wait_category_rank = 1;
END
GO
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsPlans
    @PlanIds nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT plan_id, query_plan
    FROM sys.query_store_plan
    WHERE plan_id IN
    (
        SELECT CONVERT(bigint, [value])
        FROM STRING_SPLIT(@PlanIds, ',')
    );
END
GO
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsStatisticsHealth
    @Top int
AS
BEGIN
    SET NOCOUNT ON;

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
    OUTER APPLY sys.dm_db_stats_properties(statisticsObject.object_id, statisticsObject.stats_id) AS statisticsProperties
    WHERE queryObject.is_ms_shipped = 0
        AND queryObject.type = 'U'
        AND queryTable.temporal_type <> 1
    ORDER BY
        CASE
            WHEN statisticsProperties.rows IS NULL OR statisticsProperties.rows = 0 THEN NULL
            ELSE CONVERT(float, statisticsProperties.modification_counter) / statisticsProperties.rows
        END DESC,
        statisticsProperties.modification_counter DESC,
        SCHEMA_NAME(queryObject.schema_id),
        queryObject.name,
        statisticsObject.name;
END
GO
