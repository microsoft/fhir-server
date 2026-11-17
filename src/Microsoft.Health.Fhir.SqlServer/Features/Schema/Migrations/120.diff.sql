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
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsStatisticsHealth')),
                    (OBJECT_ID(N'dbo.GetQueryStoreDiagnosticsStorage'))
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
CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsStorage
AS
BEGIN
    SET NOCOUNT ON;

    -- Require database-wide visibility rather than presenting a permission-filtered catalog as complete.
    IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) <> 1
        THROW 50403, 'Storage diagnostics requires database VIEW DEFINITION and partition statistics DMV permissions.', 1;

    ;WITH IndexStorage AS
    (
        SELECT
            queryTable.object_id,
            queryTable.schema_id,
            queryTable.name AS table_name,
            queryIndex.index_id,
            CASE WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
                THEN SUM(partitionStats.row_count) END AS approximate_index_row_count,
            CASE WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
                THEN SUM(partitionStats.used_page_count) * CONVERT(bigint, 8192) END AS used_bytes,
            CASE WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
                THEN SUM(partitionStats.reserved_page_count) * CONVERT(bigint, 8192) END AS reserved_bytes
        FROM sys.tables AS queryTable
        INNER JOIN sys.indexes AS queryIndex ON queryIndex.object_id = queryTable.object_id AND queryIndex.is_hypothetical = 0
            AND (queryIndex.is_disabled = 0 OR queryIndex.index_id = 1)
        LEFT JOIN sys.partitions AS queryPartition ON queryPartition.object_id = queryIndex.object_id AND queryPartition.index_id = queryIndex.index_id
        LEFT JOIN sys.dm_db_partition_stats AS partitionStats ON partitionStats.partition_id = queryPartition.partition_id
        WHERE queryTable.is_ms_shipped = 0
        GROUP BY queryTable.object_id, queryTable.schema_id, queryTable.name, queryTable.is_memory_optimized, queryIndex.index_id
    )
    SELECT
        SCHEMA_NAME(schema_id) AS schema_name,
        table_name,
        MAX(CASE WHEN index_id IN (0, 1) THEN approximate_index_row_count END) AS approximate_row_count,
        CASE WHEN COUNT_BIG(used_bytes) = COUNT_BIG(*) THEN SUM(used_bytes) END AS used_bytes,
        CASE WHEN COUNT_BIG(reserved_bytes) = COUNT_BIG(*) THEN SUM(reserved_bytes) END AS reserved_bytes
    FROM IndexStorage
    GROUP BY object_id, schema_id, table_name
    ORDER BY schema_name, table_name;

    SELECT
        SCHEMA_NAME(queryTable.schema_id) AS schema_name,
        queryTable.name AS table_name,
        queryIndex.index_id,
        queryIndex.name AS index_name,
        CASE WHEN queryIndex.is_disabled = 1 AND queryIndex.index_id > 1 THEN CONVERT(bigint, 0)
            WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
            THEN SUM(partitionStats.row_count) END AS approximate_index_row_count,
        CASE WHEN queryIndex.is_disabled = 1 AND queryIndex.index_id > 1 THEN CONVERT(bigint, 0)
            WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
            THEN SUM(partitionStats.used_page_count) * CONVERT(bigint, 8192) END AS used_bytes,
        CASE WHEN queryIndex.is_disabled = 1 AND queryIndex.index_id > 1 THEN CONVERT(bigint, 0)
            WHEN queryTable.is_memory_optimized = 0 AND COUNT_BIG(partitionStats.partition_id) = COUNT_BIG(*)
            THEN SUM(partitionStats.reserved_page_count) * CONVERT(bigint, 8192) END AS reserved_bytes
    FROM sys.tables AS queryTable
    INNER JOIN sys.indexes AS queryIndex ON queryIndex.object_id = queryTable.object_id AND queryIndex.is_hypothetical = 0
    LEFT JOIN sys.partitions AS queryPartition ON queryPartition.object_id = queryIndex.object_id AND queryPartition.index_id = queryIndex.index_id
    LEFT JOIN sys.dm_db_partition_stats AS partitionStats ON partitionStats.partition_id = queryPartition.partition_id
    WHERE queryTable.is_ms_shipped = 0
    GROUP BY queryTable.object_id, queryTable.schema_id, queryTable.name, queryTable.is_memory_optimized, queryIndex.index_id, queryIndex.name, queryIndex.is_disabled
    ORDER BY schema_name, table_name, queryIndex.index_id;

    ;WITH IndexPartitioning AS
    (
        SELECT
            queryTable.object_id,
            queryTable.schema_id,
            queryTable.name AS table_name,
            queryTable.is_memory_optimized,
            queryIndex.index_id,
            partitionFunction.function_id,
            partitionFunction.boundary_value_on_right,
            CASE WHEN partitionColumn.name = N'ResourceTypeId'
                    AND partitionColumn.system_type_id IN (48, 52, 56, 127)
                    AND partitionColumn.is_nullable = 0
                THEN 1 ELSE 0 END AS is_resource_partitioned
        FROM sys.tables AS queryTable
        INNER JOIN sys.indexes AS queryIndex ON queryIndex.object_id = queryTable.object_id AND queryIndex.is_hypothetical = 0
            AND (queryIndex.is_disabled = 0 OR queryIndex.index_id = 1)
        LEFT JOIN sys.partition_schemes AS partitionScheme ON partitionScheme.data_space_id = queryIndex.data_space_id
        LEFT JOIN sys.partition_functions AS partitionFunction ON partitionFunction.function_id = partitionScheme.function_id
        LEFT JOIN sys.index_columns AS partitionIndexColumn ON partitionIndexColumn.object_id = queryIndex.object_id
            AND partitionIndexColumn.index_id = queryIndex.index_id AND partitionIndexColumn.partition_ordinal = 1
        LEFT JOIN sys.columns AS partitionColumn ON partitionColumn.object_id = partitionIndexColumn.object_id
            AND partitionColumn.column_id = partitionIndexColumn.column_id
        WHERE queryTable.is_ms_shipped = 0
    ),
    PartitionStorage AS
    (
        SELECT
            queryIndex.object_id,
            queryIndex.schema_id,
            queryIndex.table_name,
            queryIndex.index_id,
            CASE
                WHEN baseIndex.is_resource_partitioned = 1 AND baseIndex.function_id IS NOT NULL
                    AND (queryIndex.function_id IS NULL OR queryIndex.function_id <> baseIndex.function_id OR queryIndex.is_resource_partitioned = 0)
                    THEN 'NonalignedIndex'
                WHEN baseIndex.is_resource_partitioned = 1 AND queryIndex.is_resource_partitioned = 1
                    AND queryIndex.function_id = baseIndex.function_id
                    THEN CASE WHEN resourceType.ResourceTypeId IS NOT NULL THEN 'ResourceType' ELSE 'SharedRange' END
                ELSE 'UnpartitionedOrOther'
            END AS attribution,
            CASE WHEN baseIndex.is_resource_partitioned = 1 AND queryIndex.is_resource_partitioned = 1
                    AND queryIndex.function_id = baseIndex.function_id
                THEN CONVERT(bigint, resourceType.ResourceTypeId) END AS resource_type_id,
            CASE WHEN baseIndex.is_resource_partitioned = 1 AND queryIndex.is_resource_partitioned = 1
                    AND queryIndex.function_id = baseIndex.function_id
                THEN resourceType.Name END AS resource_type_name,
            CASE WHEN queryIndex.is_memory_optimized = 0 THEN partitionStats.row_count END AS row_count,
            CASE WHEN queryIndex.is_memory_optimized = 0 THEN partitionStats.used_page_count * CONVERT(bigint, 8192) END AS used_bytes,
            CASE WHEN queryIndex.is_memory_optimized = 0 THEN partitionStats.reserved_page_count * CONVERT(bigint, 8192) END AS reserved_bytes
        FROM IndexPartitioning AS queryIndex
        LEFT JOIN IndexPartitioning AS baseIndex ON baseIndex.object_id = queryIndex.object_id AND baseIndex.index_id IN (0, 1)
        LEFT JOIN sys.partitions AS queryPartition ON queryPartition.object_id = queryIndex.object_id AND queryPartition.index_id = queryIndex.index_id
        LEFT JOIN sys.dm_db_partition_stats AS partitionStats ON partitionStats.partition_id = queryPartition.partition_id
        LEFT JOIN sys.partition_range_values AS lowerBoundary ON lowerBoundary.function_id = queryIndex.function_id
            AND lowerBoundary.boundary_id = queryPartition.partition_number - 1
        LEFT JOIN sys.partition_range_values AS upperBoundary ON upperBoundary.function_id = queryIndex.function_id
            AND upperBoundary.boundary_id = queryPartition.partition_number
        OUTER APPLY
        (
            SELECT TRY_CONVERT(bigint, lowerBoundary.value) AS lower_value, TRY_CONVERT(bigint, upperBoundary.value) AS upper_value
        ) AS bounds
        LEFT JOIN dbo.ResourceType AS resourceType
            ON CONVERT(decimal(38, 0), bounds.upper_value) - CONVERT(decimal(38, 0), bounds.lower_value) = 1
            AND SQL_VARIANT_PROPERTY(lowerBoundary.value, 'BaseType') IN ('tinyint', 'smallint', 'int', 'bigint')
            AND SQL_VARIANT_PROPERTY(upperBoundary.value, 'BaseType') IN ('tinyint', 'smallint', 'int', 'bigint')
            AND resourceType.ResourceTypeId = CASE WHEN queryIndex.boundary_value_on_right = 1 THEN bounds.lower_value ELSE bounds.upper_value END
    )
    SELECT
        SCHEMA_NAME(schema_id) AS schema_name,
        table_name,
        attribution,
        resource_type_id,
        resource_type_name,
        CASE WHEN COUNT_BIG(CASE WHEN index_id IN (0, 1) THEN 1 END) > 0
                AND COUNT_BIG(CASE WHEN index_id IN (0, 1) THEN row_count END) = COUNT_BIG(CASE WHEN index_id IN (0, 1) THEN 1 END)
            THEN SUM(CASE WHEN index_id IN (0, 1) THEN row_count END) END AS approximate_row_count,
        CASE WHEN COUNT_BIG(used_bytes) = COUNT_BIG(*) THEN SUM(used_bytes) END AS used_bytes,
        CASE WHEN COUNT_BIG(reserved_bytes) = COUNT_BIG(*) THEN SUM(reserved_bytes) END AS reserved_bytes
    FROM PartitionStorage
    GROUP BY object_id, schema_id, table_name, attribution, resource_type_id, resource_type_name
    HAVING MAX(CASE WHEN row_count IS NULL OR used_bytes IS NULL OR reserved_bytes IS NULL
            OR row_count <> 0 OR used_bytes <> 0 OR reserved_bytes <> 0 THEN 1 ELSE 0 END) = 1
    ORDER BY schema_name, table_name, attribution, resource_type_id;
END
GO
