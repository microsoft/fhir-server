CREATE PROCEDURE dbo.FetchResourceChanges_3
@startId BIGINT, @lastProcessedUtcDateTime DATETIME2 (7), @pageSize SMALLINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @precedingPartitionBoundary AS DATETIME2 (7) = (SELECT   TOP (1) CAST (prv.value AS DATETIME2 (7)) AS value
                                                            FROM     sys.partition_range_values AS prv WITH (NOLOCK)
                                                                     INNER JOIN
                                                                     sys.partition_functions AS pf WITH (NOLOCK)
                                                                     ON pf.function_id = prv.function_id
                                                            WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                                     AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                                     AND CAST (prv.value AS DATETIME2 (7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                            ORDER BY prv.boundary_id DESC);
    IF (@precedingPartitionBoundary IS NULL)
        BEGIN
            SET @precedingPartitionBoundary = CONVERT (DATETIME2 (7), N'1970-01-01T00:00:00.0000000');
        END
    DECLARE @endDateTimeToFilter AS DATETIME2 (7) = DATEADD(HOUR, 1, SYSUTCDATETIME());
    WITH     PartitionBoundaries
    AS       (SELECT CAST (prv.value AS DATETIME2 (7)) AS PartitionBoundary
              FROM   sys.partition_range_values AS prv WITH (NOLOCK)
                     INNER JOIN
                     sys.partition_functions AS pf WITH (NOLOCK)
                     ON pf.function_id = prv.function_id
              WHERE  pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                     AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                     AND CAST (prv.value AS DATETIME2 (7)) BETWEEN @precedingPartitionBoundary AND @endDateTimeToFilter)
    SELECT   TOP (@pageSize) Id,
                             Timestamp,
                             ResourceId,
                             ResourceTypeId,
                             ResourceVersion,
                             ResourceChangeTypeId
    FROM     PartitionBoundaries AS p CROSS APPLY (SELECT   TOP (@pageSize) Id,
                                                                            Timestamp,
                                                                            ResourceId,
                                                                            ResourceTypeId,
                                                                            ResourceVersion,
                                                                            ResourceChangeTypeId
                                                   FROM     dbo.ResourceChangeData WITH (TABLOCK, HOLDLOCK)
                                                   WHERE    Id >= @startId
                                                            AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp (Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp (p.PartitionBoundary)
                                                   ORDER BY Id ASC) AS rcd
    ORDER BY rcd.Id ASC;
END

