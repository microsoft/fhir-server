--
-- STORED PROCEDURE
--     FetchResourceChanges_3
--
-- DESCRIPTION
--     Returns the number of resource change records from startId. The start id is inclusive.
--
-- PARAMETERS
--     @startId
--         * The start id of resource change records to fetch.
--     @lastProcessedUtcDateTime
--         * The last checkpoint datetime in UTC time (Coordinated Universal Time).
--     @pageSize
--         * The page size for fetching resource change records.
--
-- RETURN VALUE
--     Resource change data rows.
--
CREATE PROCEDURE dbo.FetchResourceChanges_3
    @startId bigint,
    @lastProcessedUtcDateTime datetime2(7),
    @pageSize smallint
AS
BEGIN

    SET NOCOUNT ON;

    /* Finds the prior partition to the current partition where the last processed watermark lies.
       It is a normal scenario when a prior watermark exists. It ensures that we find a prior partition correctly when there is a gap more than hour between partitions due to system error.
       If no prior watermark exists, @precedingPartitionBoundary will be NULL. That logic is covered on the select query below. */
    DECLARE @precedingPartitionBoundary datetime2(7) = (SELECT TOP(1) CAST(prv.value as datetime2(7)) AS value FROM sys.partition_range_values AS prv
                                                            INNER JOIN sys.partition_functions AS pf ON pf.function_id = prv.function_id
                                                        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                            AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                            AND CAST(prv.value AS datetime2(7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                        ORDER BY prv.boundary_id DESC);

    DECLARE @currentUtcDateTime datetime2(7) = SYSUTCDATETIME();

    WITH PartitionBoundaries
    AS (
        /* Only grab 3 partitions with data total */
		SELECT TOP(3) CAST(prv.value as datetime2(7)) AS PartitionBoundary
		FROM sys.dm_db_partition_stats p
				INNER JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
				INNER JOIN sys.data_spaces ds ON ds.data_space_id = i.data_space_id
				INNER JOIN sys.partition_schemes ps ON ps.data_space_id = ds.data_space_id
				INNER JOIN sys.partition_functions pf ON pf.function_id = ps.function_id
				INNER JOIN sys.partition_range_values prv ON ps.function_id = prv.function_id AND prv.boundary_id = p.partition_number - 1
		WHERE p.object_id = OBJECT_ID(N'ResourceChangeData', N'TABLE') 
				AND pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
				AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
				AND (
						/* Normal logic when prior watermark exists, Grab prior partition to ensure we do not miss data that was written across a partition boundary, 
                           and includes partitions until current one to ensure we keep moving to the next partition when some partitions do not have any resource changes. */
						CAST(prv.value AS datetime2(7)) BETWEEN @precedingPartitionBoundary AND DATEADD(HOUR, 1, @currentUtcDateTime)
						OR 
                        /* Bootstrap logic when no prior watermark exists or the last processed datetime is older than the last retention datetime. 
                           The TOP above ensures we don’t have return an excessive number of partitions if we have many partitions with data for the first collection. */
						@precedingPartitionBoundary IS NULL AND CAST(prv.value AS datetime2(7)) >= CONVERT(datetime2(7), N'1970-01-01T00:00:00.0000000')
				)
				AND p.row_count > 0
		ORDER BY p.partition_number ASC
    )
    SELECT TOP(@pageSize) Id,
        Timestamp,
        ResourceId,
        ResourceTypeId,
        ResourceVersion,
        ResourceChangeTypeId
    FROM PartitionBoundaries AS p 
    CROSS APPLY (    
        /* Given the fact that Read Committed Snapshot isolation level is enabled on the FHIR database, 
           using the Repeatable Read isolation level table hint to avoid skipping resource changes 
           due to interleaved transactions on the resource change data table.
           In Repeatable Read, the select query execution will be blocked until other open transactions are completed
           for rows that match the search condition of the select statement. 
           A write transaction (update/delete) on the rows that match 
           the search condition of the select statement will wait until the current transaction is completed. 
           Other transactions can insert new rows that match the search conditions of statements issued by the current transaction.
           But, other transactions will be blocked to insert new rows during the execution of the select query, 
           and wait until the execution completes. */
        SELECT TOP(@pageSize) Id,
            Timestamp,
            ResourceId,
            ResourceTypeId,
            ResourceVersion,
            ResourceChangeTypeId
            /* If there is an existing open transaction which has not yet completed in the last 500 ms, 
               the REPEATABLEREAD table hint ensures that the select query will wait until open transactions complete. */
        FROM dbo.ResourceChangeData WITH (REPEATABLEREAD)
            WHERE Id >= @startId 
                AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(p.PartitionBoundary)
                 /* A delay is added to mitigate a risk of interleaving records under heavy concurrent transactions during the select query execution.
                    It ensures that the select query will read records which are already written in the resource change data table. */
                -- AND Timestamp <= DATEADD(millisecond, -500, @currentUtcDateTime)
        ORDER BY Id ASC
        ) AS rcd
    ORDER BY rcd.Id ASC;
END;
GO
