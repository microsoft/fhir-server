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
