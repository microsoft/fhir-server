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

    /* Finds the prior partition to the current partition where the last processed watermark lies. It is a normal scenario when a prior watermark exists. */
    DECLARE @precedingPartitionBoundary AS datetime2(7) = (SELECT TOP(1) CAST(prv.value as datetime2(7)) AS value FROM sys.partition_range_values AS prv WITH (NOLOCK)
                                                            INNER JOIN sys.partition_functions AS pf WITH (NOLOCK) ON pf.function_id = prv.function_id
                                                        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                            AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                            AND CAST(prv.value AS datetime2(7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                        ORDER BY prv.boundary_id DESC);

    IF (@precedingPartitionBoundary IS NULL)
    BEGIN
        /* It happens when no prior watermark exists or the last processed datetime of prior watermark is older than the last retention datetime.
           Uses the partition anchor datetime as the last processed DateTime. */
        SET @precedingPartitionBoundary = CONVERT (DATETIME2 (7), N'1970-01-01T00:00:00.0000000');
    END;

    /* It ensures that it will not check resource changes in future partitions. */
    DECLARE @endDateTimeToFilter AS datetime2(7) = DATEADD(HOUR, 1, SYSUTCDATETIME());

    WITH PartitionBoundaries
    AS (
        /* Normal logic when prior watermark exists, grab prior partition to ensure we do not miss data that was written across a partition boundary,
           and includes partitions until current one to ensure we keep moving to the next partition when some partitions do not have any resource changes. */
        SELECT CAST(prv.value as datetime2(7)) AS PartitionBoundary FROM sys.partition_range_values AS prv WITH (NOLOCK)
            INNER JOIN sys.partition_functions AS pf WITH (NOLOCK) ON pf.function_id = prv.function_id
        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
            AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
            AND CAST(prv.value AS datetime2(7)) BETWEEN @precedingPartitionBoundary AND @endDateTimeToFilter
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
           using TABLOCK and HOLDLOCK table hints to avoid skipping resource changes 
           due to interleaved transactions on the resource change data table. */
        SELECT TOP(@pageSize) Id,
            Timestamp,
            ResourceId,
            ResourceTypeId,
            ResourceVersion,
            ResourceChangeTypeId
        /* Acquires and holds the table lock to prevent new resource changes from being created during the select query execution.
           Without the TABLOCK and HOLDLOCK hints, the lock will be applied at the row or page level which can result in missed reads
           if an insert occurs in range that was previously read and the locks were released on the read. */
        FROM dbo.ResourceChangeData WITH (TABLOCK, HOLDLOCK)
            WHERE Id >= @startId
                AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(p.PartitionBoundary)
        ORDER BY Id ASC
        ) AS rcd
    ORDER BY rcd.Id ASC;
END;
GO
