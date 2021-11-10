-- STORED PROCEDURE
--     RemovePartitionFromResourceChanges
--
-- DESCRIPTION
--     Switches a partition from a resource change data table to a staging table.
--     Then merges two partitions based on a given partition boundary value.
--     After that, truncates the staging table to purge the old resource change data.
--
--     @partitionNumberToSwtichOut
--         * The partition number to switch out.
--     @partitionBoundaryToMerge
--         * The partition boundary value to merge.
--
CREATE OR ALTER PROCEDURE dbo.RemovePartitionFromResourceChanges
    @partitionNumberToSwitchOut int,
    @partitionBoundaryToMerge datetime2(7)
AS
  BEGIN
    
    /* using XACT_ABORT to force a rollback on any error. */
    SET XACT_ABORT ON;
    
    BEGIN TRANSACTION

        /* Cleans up a staging table if there are existing rows. */
        TRUNCATE TABLE dbo.ResourceChangeDataStaging;
        
        /* Switches a partition to the staging table. */
        ALTER TABLE dbo.ResourceChangeData SWITCH PARTITION @partitionNumberToSwitchOut TO dbo.ResourceChangeDataStaging;
        
        /* Merges range to move lower boundary one partition ahead. */
        ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp() MERGE RANGE(@partitionBoundaryToMerge);
        
        /* Cleans up the staging table to purge resource changes. */
        TRUNCATE TABLE dbo.ResourceChangeDataStaging;

    COMMIT TRANSACTION;
END;
GO 
