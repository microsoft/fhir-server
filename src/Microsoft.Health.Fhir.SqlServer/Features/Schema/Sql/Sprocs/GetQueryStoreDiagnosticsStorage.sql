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
