-- STORED PROCEDURE
--     RemovePartitionFromResourceChanges_2
--
-- DESCRIPTION
--     Switches a partition from a resource change data table to a staging table.
--     Then merges two partitions based on a given partition boundary value.
--     After that, truncates the staging table to purge the old resource change data.
--     The RANGE RIGHT partition is used on ResourceChangeData table.
--     For the sliding window scenario, if you want to drop the left-most partition,
--     a partitionBoundaryToMerge argument should be the lowest partition boundary value and
--     a partitionNumberToSwtichOut argument should be 2 assuming that partition 1 is always empty.
--
--     @partitionNumberToSwtichOut
--         * The partition number to switch out.
--     @partitionBoundaryToMerge
--         * The partition boundary value to merge.
--
CREATE OR ALTER PROCEDURE dbo.RemovePartitionFromResourceChanges_2
    @partitionNumberToSwitchOut int,
    @partitionBoundaryToMerge datetime2(7)
AS
  BEGIN
    /* Cleans up a staging table if there are existing rows. */
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
        
    /* Switches a partition to the staging table. */
    ALTER TABLE dbo.ResourceChangeData SWITCH PARTITION @partitionNumberToSwitchOut TO dbo.ResourceChangeDataStaging;
        
    /* Merges range to move boundary one partition ahead. */
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp() MERGE RANGE(@partitionBoundaryToMerge);
        
    /* Cleans up the staging table to purge resource changes. */
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
END;
GO 
