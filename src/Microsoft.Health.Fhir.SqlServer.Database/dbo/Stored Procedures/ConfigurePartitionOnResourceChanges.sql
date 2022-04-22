CREATE   PROCEDURE dbo.ConfigurePartitionOnResourceChanges
@numberOfFuturePartitionsToAdd INT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @partitionBoundary AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    DECLARE @startingRightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                                      FROM     sys.partition_range_values AS prv
                                                                               INNER JOIN
                                                                               sys.partition_functions AS pf
                                                                               ON pf.function_id = prv.function_id
                                                                      WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                                      ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
    DECLARE @numberOfPartitionsToAdd AS INT = @numberOfFuturePartitionsToAdd + 1;
    WHILE @numberOfPartitionsToAdd > 0
        BEGIN
            IF (@startingRightPartitionBoundary < @partitionBoundary)
                BEGIN
                    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [PRIMARY];
                    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                        SPLIT RANGE (@partitionBoundary);
                END
            SET @partitionBoundary = DATEADD(hour, 1, @partitionBoundary);
            SET @numberOfPartitionsToAdd -= 1;
        END
    COMMIT TRANSACTION;
END

