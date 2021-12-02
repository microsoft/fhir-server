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

    IF (@lastProcessedUtcDateTime IS NULL) BEGIN
        /* Sets the partition anchor datetime as the last processed DateTime. */
        SET @lastProcessedUtcDateTime = CONVERT(datetime2(7), N'1970-01-01T00:00:00.0000000');
    END;

    /* Finds the prior partition to the current partition where the last processed watermark lies. It is a normal scenario when a prior watermark exists. */
    DECLARE @precedingPartitionBoundary datetime2(7) = (SELECT TOP(1) CAST(prv.value as datetime2(7)) AS value FROM sys.partition_range_values AS prv
                                                               INNER JOIN sys.partition_functions AS pf ON pf.function_id = prv.function_id
                                                           WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                               AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                               AND CAST(prv.value AS datetime2(7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                           ORDER BY prv.boundary_id DESC);

    IF (@precedingPartitionBoundary IS NULL) BEGIN
        /* It happens when no prior watermark exists or the last processed datetime is older than the last retention datetime.
           Substracts one hour from the rounded DateTime to include a partition where the watermark lies, in the partition boundary filter below. */
        SET @precedingPartitionBoundary = DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime) - 1, 0);
    END;
    
    DECLARE @endDateTimeToFilter datetime2(7) = DATEADD(HOUR, 1, SYSUTCDATETIME());

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
    WITH PartitionBoundaries
    AS (
        SELECT CAST(prv.value as datetime2(7)) AS PartitionBoundary FROM sys.partition_range_values AS prv
            INNER JOIN sys.partition_functions AS pf ON pf.function_id = prv.function_id
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
        SELECT TOP(@pageSize) Id,
            Timestamp,
            ResourceId,
            ResourceTypeId,
            ResourceVersion,
            ResourceChangeTypeId
        FROM dbo.ResourceChangeData WITH (REPEATABLEREAD)
            WHERE Id >= @startId AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(p.PartitionBoundary)
        ORDER BY Id ASC
        ) AS rcd
    ORDER BY rcd.Id ASC;
END;
GO
