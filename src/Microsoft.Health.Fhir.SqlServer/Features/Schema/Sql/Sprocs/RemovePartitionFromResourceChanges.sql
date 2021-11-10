
-- STORED PROCEDURE
--     RemovePartitionFromResourceChanges
--
-- DESCRIPTION
--     Switches out and merges the extreme left partition with the immediate left partition.
--     After that, truncates the staging table to purge the old resource change data.
--
--     @partitionBoundary
--         * The output parameter to stores the removed partition boundary.
--
CREATE PROCEDURE dbo.RemovePartitionFromResourceChanges
    @partitionBoundary datetime2(7) OUTPUT
AS
  BEGIN
    
    /* using XACT_ABORT to force a rollback on any error. */
    SET XACT_ABORT ON;
    
    BEGIN TRANSACTION
    
        /* Finds the lowest boundary value. */
        DECLARE @leftPartitionBoundary datetime2(7) = CAST((SELECT TOP (1) value
                            FROM sys.partition_range_values AS prv
                                JOIN sys.partition_functions AS pf
                                    ON pf.function_id = prv.function_id
                            WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                            ORDER BY prv.boundary_id ASC) AS datetime2(7));

        /* Cleans up a staging table if there are existing rows. */
        TRUNCATE TABLE dbo.ResourceChangeDataStaging;
        
        /* Switches a partition to the staging table. */
        ALTER TABLE dbo.ResourceChangeData SWITCH PARTITION 2 TO dbo.ResourceChangeDataStaging;
        
        /* Merges range to move lower boundary one partition ahead. */
        ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp() MERGE RANGE(@leftPartitionBoundary);
        
        /* Cleans up the staging table to purge resource changes. */
        TRUNCATE TABLE dbo.ResourceChangeDataStaging;
        
        SET @partitionBoundary = @leftPartitionBoundary;

    COMMIT TRANSACTION
END;
GO 
