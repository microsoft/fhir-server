CREATE   PROCEDURE dbo.AddPartitionOnResourceChanges
@partitionBoundary DATETIME2 (7) OUTPUT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @rightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                              FROM     sys.partition_range_values AS prv
                                                                       INNER JOIN
                                                                       sys.partition_functions AS pf
                                                                       ON pf.function_id = prv.function_id
                                                              WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                              ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
    DECLARE @timestamp AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    IF (@rightPartitionBoundary < @timestamp)
        BEGIN
            SET @rightPartitionBoundary = @timestamp;
        END
    SET @rightPartitionBoundary = DATEADD(hour, 1, @rightPartitionBoundary);
    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        SPLIT RANGE (@rightPartitionBoundary);
    SET @partitionBoundary = @rightPartitionBoundary;
    COMMIT TRANSACTION;
END

