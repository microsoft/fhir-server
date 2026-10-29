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
