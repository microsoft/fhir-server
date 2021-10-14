/*
We are making the following changes in this version of the schema
-- Fixed issue with AcquiredExportJobs throwing an exception if the calculated limit was 0 or negative. 
*/

IF NOT EXISTS(SELECT * FROM sys.partition_functions WHERE name = 'PartitionFunction_ResourceChangeData_Timestamp')
BEGIN 
    -- Partition function for the ResourceChangeData table.
    -- It is not a fixed-sized partition. It is a sliding window partition.
    -- Adding a range right partition function on a timestamp column. 
    -- Range right means that the actual boundary value belongs to its right partition,
    -- it is the first value in the right partition.
    CREATE PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp (datetime2(7))
        AS RANGE RIGHT FOR VALUES('1970-01-01T00:00:00.0000000');
END;

IF NOT EXISTS(SELECT * FROM sys.partition_schemes WHERE name = 'PartitionScheme_ResourceChangeData_Timestamp')
BEGIN 
    -- Partition scheme which uses a partition function called PartitionFunction_ResourceChangeData_Timestamp,
    -- and places partitions on the PRIMARY filegroup.
    CREATE PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp
        AS PARTITION PartitionFunction_ResourceChangeData_Timestamp ALL TO([PRIMARY]);
END; 

IF (EXISTS(SELECT * FROM sys.partition_functions WHERE name = 'PartitionFunction_ResourceChangeData_Timestamp')
	AND EXISTS(SELECT * FROM sys.partition_schemes WHERE name = 'PartitionScheme_ResourceChangeData_Timestamp'))
BEGIN
   -- Creates initial partitions based on default 48-hour retention period.
    DECLARE @numberOfPartitions int = 48;
    DECLARE @rightPartitionBoundary datetime2(7);
    DECLARE @currentDateTime datetime2(7) = sysutcdatetime();
		
	-- There will be 51 partition boundaries and 52 partitions, 48 partitions for history,
    -- one for the current hour, one for the next hour, and 2 partitions for start and end.
    WHILE @numberOfPartitions >= -1 
    BEGIN
        -- Rounds the start datetime to the hour.
        SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfPartitions, 0);

		-- Checks if a partition exists.
		IF NOT EXISTS (SELECT 1 value FROM sys.partition_range_values AS prv
						JOIN sys.partition_functions AS pf
							ON pf.function_id = prv.function_id
					WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
						and CONVERT(datetime2(7), prv.value, 126) = @rightPartitionBoundary) 
		BEGIN
			-- Creates new empty partition by creating new boundary value and specifying NEXT USED file group.
			ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
			ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp() SPLIT RANGE(@rightPartitionBoundary);
		END;
            
        SET @numberOfPartitions -= 1;
    END;
END;

-- Creates a staging table 
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ResourceChangeDataStaging')
BEGIN
    -- Staging table that will be used for partition switch out. 
    CREATE TABLE dbo.ResourceChangeDataStaging 
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        Timestamp datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeDataStaging_Timestamp DEFAULT sysutcdatetime(),
        ResourceId varchar(64) NOT NULL,
        ResourceTypeId smallint NOT NULL,
        ResourceVersion int NOT NULL,
        ResourceChangeTypeId tinyint NOT NULL,
        CONSTRAINT PK_ResourceChangeDataStaging_TimestampId PRIMARY KEY (Timestamp, Id)
    ) ON [PRIMARY]
END;
GO

-- Adds a check constraint on the staging table for a partition boundary validation.
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_ResourceChangeDataStaging_partition')
BEGIN    
    ALTER TABLE dbo.ResourceChangeDataStaging WITH CHECK 
        ADD CONSTRAINT CHK_ResourceChangeDataStaging_partition CHECK(Timestamp < CONVERT(DATETIME2(7), '9999-12-31 23:59:59.9999999'));
END;
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_ResourceChangeDataStaging_partition')
BEGIN    
    ALTER TABLE dbo.ResourceChangeDataStaging CHECK CONSTRAINT CHK_ResourceChangeDataStaging_partition;
END;
GO

    
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'PK_ResourceChangeData')
BEGIN
    -- Drops index.
    ALTER TABLE dbo.ResourceChangeData DROP CONSTRAINT PK_ResourceChangeData WITH (ONLINE = OFF);
END;
        
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'PK_ResourceChangeData_TimestampId')
BEGIN    
    -- Adds primary key clustered index.
    ALTER TABLE dbo.ResourceChangeData ADD CONSTRAINT PK_ResourceChangeData_TimestampId
        PRIMARY KEY CLUSTERED(Timestamp ASC, Id ASC) ON PartitionScheme_ResourceChangeData_Timestamp(Timestamp)
END;
GO

/*************************************************************
    Purge partition feature for resource change data
**************************************************************/
--
-- STORED PROCEDURE
--     Acquires export jobs.
--
-- DESCRIPTION
--     Timestamps the available export jobs and sets their statuses to running.
--
-- PARAMETERS
--     @jobHeartbeatTimeoutThresholdInSeconds
--         * The number of seconds that must pass before an export job is considered stale
--     @maximumNumberOfConcurrentJobsAllowed
--         * The maximum number of running jobs we can have at once
--
-- RETURN VALUE
--     The updated jobs that are now running.
--
CREATE or ALTER PROCEDURE dbo.AcquireExportJobs
    @jobHeartbeatTimeoutThresholdInSeconds bigint,
    @maximumNumberOfConcurrentJobsAllowed int
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON
    
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION
    
    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())
    
    -- Get the number of jobs that are running and not stale.
    -- Acquire and hold an exclusive table lock for the entire transaction to prevent jobs from being created, updated or deleted during acquisitions.
    DECLARE @numberOfRunningJobs int
    SELECT @numberOfRunningJobs = COUNT(*) FROM dbo.ExportJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > @expirationDateTime
    
    -- Determine how many available jobs we can pick up.
    DECLARE @limit int = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;
            
    IF (@limit > 0) BEGIN

        DECLARE @availableJobs TABLE (Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL, JobVersion binary(8) NOT NULL)
        
        -- Get the available jobs, which are export jobs that are queued or stale.
        -- Older jobs will be prioritized over newer ones.
        INSERT INTO @availableJobs
        SELECT TOP(@limit) Id, JobVersion
        FROM dbo.ExportJob
        WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime
                            
        DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()
        
        -- Update each available job's status to running both in the export table's status column and in the raw export job record JSON.
        UPDATE dbo.ExportJob
        SET Status = 'Running', HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = JSON_MODIFY(RawJobRecord,'$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM dbo.ExportJob job INNER JOIN @availableJobs availableJob ON job.Id = availableJob.Id AND job.JobVersion = availableJob.JobVersion
        
    END

    COMMIT TRANSACTION
END;
GO 

--
-- STORED PROCEDURE
--     FetchResourceChanges_2
--
-- DESCRIPTION
--     Returns the number of resource change records from startId. The start id is inclusive.
--
-- PARAMETERS
--     @startId
--         * The start id of resource change records to fetch.
--     @@lastProcessedDateTime
--         * The last checkpoint datetime.
--     @pageSize
--         * The page size for fetching resource change records.
--
-- RETURN VALUE
--     Resource change data rows.
--
CREATE OR ALTER PROCEDURE dbo.FetchResourceChanges_2
    @startId bigint,
    @lastProcessedDateTime datetime2(7),
    @pageSize smallint
AS
BEGIN

    SET NOCOUNT ON;
    
    -- Given the fact that Read Committed Snapshot isolation level is enabled on the FHIR database,
    -- using the Repeatable Read isolation level table hint to avoid skipping resource changes
    -- due to interleaved transactions on the resource change data table. 
    -- In Repeatable Read, the select query execution will be blocked until other open transactions are completed
    -- for rows that match the search condition of the select statement. 
    -- A write transaction (update/delete) on the rows that match 
    -- the search condition of the select statement will wait until the read transaction is completed.
    -- But, other transactions can insert new rows.
    SELECT TOP(@pageSize) Id,
      Timestamp,
      ResourceId,
      ResourceTypeId,
      ResourceVersion,
      ResourceChangeTypeId
      FROM dbo.ResourceChangeData WITH (REPEATABLEREAD)
    WHERE Timestamp >= @lastProcessedDateTime and Id >= @startId 
    ORDER BY Timestamp ASC, Id ASC;
END
GO
