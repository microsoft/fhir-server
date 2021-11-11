/***********************************************************************
 NOTE: just checking first object, since this is run in transaction
***************************************************************************/
IF EXISTS (
    SELECT *
    FROM sys.tables
    WHERE name = 'ClaimType')
BEGIN
    ROLLBACK TRANSACTION
    RETURN
END

/*************************************************************
    Schema Version - Make sure to update the version here for new migration
**************************************************************/
Go

INSERT INTO dbo.SchemaVersion
VALUES
    (20, 'started')

Go

/*************************************************************
    Resource change capture feature
**************************************************************/

/*************************************************************
    Resource change data table
**************************************************************/

/* Partition function for the ResourceChangeData table.
   It is not a fixed-sized partition. It is a sliding window partition.
   Adding a range right partition function on a timestamp column. 
   Range right means that the actual boundary value belongs to its right partition,
   it is the first value in the right partition.
   Partition anchor DateTime can be any past DateTime that is not in the retention period.
   So, January 1st, 1970 at 00:00:00 UTC is chosen as the initial partition anchor DateTime
   in the resource change data partition function. */
CREATE PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp (datetime2(7))
AS RANGE RIGHT FOR VALUES(N'1970-01-01T00:00:00.0000000');

/* Partition scheme which uses a partition function called PartitionFunction_ResourceChangeData_Timestamp,
   and places partitions on the PRIMARY filegroup. */
CREATE PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp AS PARTITION PartitionFunction_ResourceChangeData_Timestamp ALL TO([PRIMARY]);

/* Creates initial partitions based on default 48-hour retention period and 1-month future partitions. */
DECLARE @numberOfHistoryPartitions int = 48;

/* To have a buffer time when an error occurs related to partition creation, 
   by default 720 hours of partitions for the future DateTime will be created in the resource change data table. 
   The number of future partitions is 720 for 30 days. */
DECLARE @numberOfFuturePartitions int = 720;
DECLARE @rightPartitionBoundary datetime2(7);
DECLARE @currentDateTime datetime2(7) = sysutcdatetime();

/* There will be 771 partitions, and 770 partition boundaries, one for partition anchor DateTime,
   48 partition boundaries for history, one for the current hour, and 720 for the future datetimes.
   Creates 720 partition boudaries for the future to mitigate risk to any data movement
   and have a buffer time to investigate an issue when an error occurs on partition creation.
   Once a database is initialized, a purge change data worker will be run hourly to maintain the number of partitions on resource change datatable. 
   The partition anchor boundary will be removed at the very first run of the purge operation of the purge change data worker.
   Total number of partition boundaries = the number of history partitions + one for the current hour + the number of future partitions. */
WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
BEGIN        
    /* Rounds the start datetime to the hour. */
    SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);
            
    /* Creates new empty partition by creating new boundary value and specifying NEXT USED file group. */
    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp() SPLIT RANGE(@rightPartitionBoundary); 
            
    SET @numberOfHistoryPartitions -= 1;
END;

