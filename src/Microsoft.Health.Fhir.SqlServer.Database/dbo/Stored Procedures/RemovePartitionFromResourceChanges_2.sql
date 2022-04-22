CREATE   PROCEDURE dbo.RemovePartitionFromResourceChanges_2
@partitionNumberToSwitchOut INT, @partitionBoundaryToMerge DATETIME2 (7)
AS
BEGIN
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
    ALTER TABLE dbo.ResourceChangeData SWITCH PARTITION @partitionNumberToSwitchOut TO dbo.ResourceChangeDataStaging;
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        MERGE RANGE (@partitionBoundaryToMerge);
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
END

