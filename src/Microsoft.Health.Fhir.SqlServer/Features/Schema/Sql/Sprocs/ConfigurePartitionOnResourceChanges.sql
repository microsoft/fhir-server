/*************************************************************
    Purge partition feature for resource change data
**************************************************************/
--
-- STORED PROCEDURE
--     ConfigurePartitionOnResourceChanges_2
--
-- DESCRIPTION
--     Creates initial partitions for future datetimes on the resource change data table if they do not already exist.
--
-- PARAMETERS
--     @numberOfFuturePartitionsToAdd
--         * The number of partitions to add for future datetimes.
--
CREATE PROCEDURE dbo.ConfigurePartitionOnResourceChanges_2
    @numberOfFuturePartitionsToAdd int
AS
BEGIN
    
    /* using XACT_ABORT to force a rollback on any error. */
    SET XACT_ABORT ON;
        
    /* Rounds the current datetime to the hour. */
    DECLARE @partitionBoundary AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    
    /* Adds one due to starting from the current hour. */
    DECLARE @numberOfPartitionsToAdd AS INT = @numberOfFuturePartitionsToAdd + 1;
    
    /* Creates the partitions for future datetimes on the resource change data table. */    
    WHILE @numberOfPartitionsToAdd > 0
    BEGIN
        BEGIN TRANSACTION
            /* Checks if a partition exists. */
            IF NOT EXISTS (SELECT 1 FROM sys.partition_range_values AS prv
                                INNER JOIN sys.partition_functions AS pf ON pf.function_id = prv.function_id
                            WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                AND CAST(prv.value AS datetime2(7)) = @partitionBoundary)
            BEGIN
                ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [PRIMARY];
                ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                    SPLIT RANGE (@partitionBoundary);
            END;
        COMMIT TRANSACTION;
            
        /* Adds one hour for the next partition. */
        SET @partitionBoundary = DATEADD(hour, 1, @partitionBoundary);
        SET @numberOfPartitionsToAdd -= 1;
    END;
END;
GO
