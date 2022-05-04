
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
GO

INSERT INTO dbo.SchemaVersion
VALUES
    (30, 'started')
GO

--
-- STORED PROCEDURE
--     AddPartitionOnResourceChanges
--
-- DESCRIPTION
--     Creates a new partition at the right for the future date which will be
--     the next hour of the right-most partition boundry.
--
-- PARAMETERS
--     @partitionBoundary
--         * The output parameter to stores the added partition boundary.
--
CREATE OR ALTER PROCEDURE dbo.AddPartitionOnResourceChanges
    @partitionBoundary datetime2(7) OUTPUT
AS
BEGIN
	
	/* using XACT_ABORT to force a rollback on any error. */
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
	
	/* Finds the highest boundary value */
    DECLARE @rightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                              FROM     sys.partition_range_values AS prv
                                                                       INNER JOIN
                                                                       sys.partition_functions AS pf
                                                                       ON pf.function_id = prv.function_id
                                                              WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                              ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
															  
	/* Rounds the current datetime to the hour. */
    DECLARE @timestamp AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    
	/* Ensures the next boundary value is greater than the current datetime. */
	IF (@rightPartitionBoundary < @timestamp)
        BEGIN
            SET @rightPartitionBoundary = @timestamp;
        END
    
	/* Adds one hour for the next partition. */
	SET @rightPartitionBoundary = DATEADD(hour, 1, @rightPartitionBoundary);
    
	/* Creates new empty partition by creating new boundary value and specifying NEXT USED file group. */
	ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        SPLIT RANGE (@rightPartitionBoundary);
    SET @partitionBoundary = @rightPartitionBoundary;
    COMMIT TRANSACTION;
END
GO

/*************************************************************
    Purge partition feature for resource change data
**************************************************************/
--
-- STORED PROCEDURE
--     ConfigurePartitionOnResourceChanges
--
-- DESCRIPTION
--     Creates initial partitions for future datetimes on the resource change data table if they do not already exist.
--
-- PARAMETERS
--     @numberOfFuturePartitionsToAdd
--         * The number of partitions to add for future datetimes.
--
CREATE OR ALTER PROCEDURE dbo.ConfigurePartitionOnResourceChanges
    @numberOfFuturePartitionsToAdd int
AS
BEGIN
    
	/* using XACT_ABORT to force a rollback on any error. */
	SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    
	/* Creates the partitions for future datetimes on the resource change data table. */    
        
    /* Rounds the current datetime to the hour. */
	DECLARE @partitionBoundary AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    
	/* Finds the highest boundary value. */
	DECLARE @startingRightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                                      FROM     sys.partition_range_values AS prv
                                                                               INNER JOIN
                                                                               sys.partition_functions AS pf
                                                                               ON pf.function_id = prv.function_id
                                                                      WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                                      ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
    
	/* Adds one due to starting from the current hour. */
	DECLARE @numberOfPartitionsToAdd AS INT = @numberOfFuturePartitionsToAdd + 1;
    WHILE @numberOfPartitionsToAdd > 0
        BEGIN
            /* Checks if a partition exists. */
			IF (@startingRightPartitionBoundary < @partitionBoundary)
                BEGIN
                    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [PRIMARY];
                    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                        SPLIT RANGE (@partitionBoundary);
                END
            
			/* Adds one hour for the next partition. */
			SET @partitionBoundary = DATEADD(hour, 1, @partitionBoundary);
            SET @numberOfPartitionsToAdd -= 1;
        END
    COMMIT TRANSACTION;
END
GO

--
-- STORED PROCEDURE
--     GetEventAgentCheckpoint
--
-- DESCRIPTION
--     Gets a checkpoint for an Event Agent
--
-- PARAMETERS
--     @Id
--         * The identifier of the checkpoint.
--
-- RETURN VALUE
--     A checkpoint for the given checkpoint id, if one exists.
--
CREATE OR ALTER PROCEDURE dbo.FetchEventAgentCheckpoint
    @CheckpointId varchar(64)
AS
BEGIN
    SELECT TOP(1) CheckpointId, LastProcessedDateTime, LastProcessedIdentifier
    FROM dbo.EventAgentCheckpoint
    WHERE CheckpointId = @CheckpointId
END
GO

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

/*************************************************************
    Stored procedures for getting and setting checkpoints
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateEventAgentCheckpoint
--
-- DESCRIPTION
--     Sets a checkpoint for an Event Agent
--
-- PARAMETERS
--     @CheckpointId
--         * The identifier of the checkpoint.
--     @LastProcessedDateTime
--         * The datetime of last item that was processed.
--     @LastProcessedIdentifier
--         *The identifier of the last item that was processed.
--
-- RETURN VALUE
--     It does not return a value.
--
CREATE OR ALTER PROCEDURE dbo.UpdateEventAgentCheckpoint
    @CheckpointId varchar(64),
    @LastProcessedDateTime datetimeoffset(7) = NULL,
    @LastProcessedIdentifier varchar(64) = NULL
AS
BEGIN
    IF EXISTS (SELECT * FROM dbo.EventAgentCheckpoint WHERE CheckpointId = @CheckpointId)
    UPDATE dbo.EventAgentCheckpoint SET CheckpointId = @CheckpointId, LastProcessedDateTime = @LastProcessedDateTime, LastProcessedIdentifier = @LastProcessedIdentifier, UpdatedOn = sysutcdatetime()
    WHERE CheckpointId = @CheckpointId
    ELSE
    INSERT INTO dbo.EventAgentCheckpoint
        (CheckpointId, LastProcessedDateTime, LastProcessedIdentifier, UpdatedOn)
    VALUES
        (@CheckpointId, @LastProcessedDateTime, @LastProcessedIdentifier, sysutcdatetime())
END
GO

ALTER TABLE dbo.CompartmentAssignment SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

/*************************************************************
    Event Agent checkpoint feature
**************************************************************/

/*************************************************************
    Event Agent checkpoint table
**************************************************************/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EventAgentCheckpoint')
BEGIN
    --The following statement was imported into the database project as a schema object and named dbo.EventAgentCheckpoint.
--CREATE TABLE dbo.EventAgentCheckpoint
--    (
--        CheckpointId varchar(64) NOT NULL,
--        LastProcessedDateTime datetimeoffset(7),
--        LastProcessedIdentifier varchar(64),
--        UpdatedOn datetime2(7) NOT NULL DEFAULT sysutcdatetime(),
--        CONSTRAINT PK_EventAgentCheckpoint PRIMARY KEY CLUSTERED (CheckpointId)
--    )
--    ON [PRIMARY]

END
GO

ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.QuantitySearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )
GO

INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (0, N'Creation')
GO

INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (1, N'Update')
GO

INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (2, N'Deletion')
GO

ALTER TABLE dbo.StringSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenStringCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenText SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO

ALTER TABLE dbo.UriSearchParam SET ( LOCK_ESCALATION = AUTO )
GO


/*************************************************************
    Partitioning function and scheme
**************************************************************/

GO


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


GO

/*************************************************************
    Sequence for generating unique 12.5ns "tick" components that are added
    to a base ID based on the timestamp to form a unique resource surrogate ID
**************************************************************/

GO

/*************************************************************
    Stored procedures for complete task with result
**************************************************************/
--
-- STORED PROCEDURE
--     CompleteTask
--
-- DESCRIPTION
--     Complete the task and update task result.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskResult
--         * The result for the task execution
--     @runId
--         * Current runId for this exuction of the task
--

GO

/*************************************************************
    Stored procedures for get next available task
**************************************************************/
--
-- STORED PROCEDURE
--     GetNextTask
--
-- DESCRIPTION
--     Get next available task
--
-- PARAMETERS
--     @queueId
--         * The ID of the task record
--     @taskHeartbeatTimeoutThresholdInSeconds
--         * Timeout threshold in seconds for heart keep alive
--

GO


/*************************************************************
    Stored procedures for reset task
**************************************************************/
--
-- STORED PROCEDURE
--     ResetTask
--
-- DESCRIPTION
--     Reset the task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @runId
--         * Current runId for this exuction of the task
--     @result
--         * The result of the task
--


GO
