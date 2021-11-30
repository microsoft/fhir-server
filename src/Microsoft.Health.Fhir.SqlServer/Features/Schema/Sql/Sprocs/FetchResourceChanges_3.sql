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
--     @partitionUtcDatetime
--         * The partition datetime to look up, which needs to be rounded down to the nearest hour.
--     @pageSize
--         * The page size for fetching resource change records.
--
-- RETURN VALUE
--     Resource change data rows.
--
CREATE PROCEDURE dbo.FetchResourceChanges_3
    @startId bigint,
    @partitionUtcDatetime datetime2(7),
    @pageSize smallint
AS
BEGIN

    SET NOCOUNT ON;

    DECLARE @partitions TABLE (partitionBoundary datetime2(7));
    
    INSERT INTO @partitions 
        SELECT CAST(prv.value AS datetime2(7)) FROM sys.partition_range_values AS prv
            INNER JOIN sys.partition_functions AS pf ON pf.function_id = prv.function_id
        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
              AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(CAST(prv.value AS datetime2(7))) >= $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(@partitionUtcDatetime)
              AND CAST(prv.value AS datetime2(7)) < DATEADD(hour, 1, SYSUTCDATETIME());

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
    FROM @partitions AS p CROSS APPLY (
        SELECT TOP(@pageSize) * FROM ResourceChangeData WITH (REPEATABLEREAD)
            WHERE $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(p.partitionBoundary)
                AND Id >= @startId
        ORDER BY Id ASC
        ) AS cd
    ORDER BY cd.Id ASC;
END;
GO
