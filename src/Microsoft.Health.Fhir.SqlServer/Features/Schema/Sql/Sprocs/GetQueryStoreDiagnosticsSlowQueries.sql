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
