
/*************************************************************
    Partitioning function and scheme
**************************************************************/
Go

CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId (smallint)
AS RANGE RIGHT FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);

CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId
AS PARTITION PartitionFunction_ResourceTypeId ALL TO ([PRIMARY]);

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

Go
