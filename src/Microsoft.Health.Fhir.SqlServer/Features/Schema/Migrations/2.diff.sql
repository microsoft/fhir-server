/*************************************************************
    Export Job
**************************************************************/
CREATE TABLE dbo.ExportJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Hash varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ExportJob ON dbo.ExportJob
(
    Id
)

CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob_Hash_Status_HeartbeatDateTime ON dbo.ExportJob
(
    Hash,
    Status,
    HeartbeatDateTime
)

GO

/*************************************************************
    Stored procedures for exporting
**************************************************************/
--
-- STORED PROCEDURE
--     Creates an export job.
--
-- DESCRIPTION
--     Creates a new row to the ExportJob table, adding a new job to the queue of jobs to be processed.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record
--     @hash
--         * The SHA256 hash of the export job record ID
--     @status
--         * The status of the export job
--     @rawJobRecord
--         * A JSON document
--
-- RETURN VALUE
--     The row version of the created export job.
--
CREATE PROCEDURE dbo.CreateExportJob
    @id varchar(64),
    @hash varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    INSERT INTO dbo.ExportJob
        (Id, Hash, Status, HeartbeatDateTime, RawJobRecord)
    VALUES
        (@id, @hash, @status, @heartbeatDateTime, @rawJobRecord)
  
    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets an export job given its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record to retrieve
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Id = @id
GO

--
-- STORED PROCEDURE
--     Gets an export job given the hash of its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching hash.
--
-- PARAMETERS
--     @hash
--         * The SHA256 hash of the export job record ID
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobByHash
    @hash varchar(64)
AS
    SET NOCOUNT ON

    SELECT TOP(1) RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Hash = @hash AND (Status = 'Queued' OR Status = 'Running')
    ORDER BY HeartbeatDateTime ASC
GO

--
-- STORED PROCEDURE
--     Updates an export job.
--
-- DESCRIPTION
--     Modifies an existing job in the ExportJob table.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record
--     @status
--         * The status of the export job
--     @rawJobRecord
--         * A JSON document
--     @jobVersion
--         * The version of the job to update must match this
--
-- RETURN VALUE
--     The row version of the updated export job.
--
CREATE PROCEDURE dbo.UpdateExportJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max),
    @jobVersion binary(8)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @currentJobVersion binary(8)

    -- Acquire and hold an update lock on a row in the ExportJob table for the entire transaction.
    -- This ensures the version check and update occur atomically.
    SELECT @currentJobVersion = JobVersion
    FROM dbo.ExportJob WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @id

    IF (@currentJobVersion IS NULL) BEGIN
        THROW 50404, 'Export job record not found', 1;
    END

    IF (@jobVersion <> @currentJobVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.ExportJob
    SET Status = @status, HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = @rawJobRecord
    WHERE Id = @id
  
    SELECT MIN_ACTIVE_ROWVERSION()

    COMMIT TRANSACTION
GO

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
CREATE PROCEDURE dbo.AcquireExportJobs
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
   
    COMMIT TRANSACTION
GO
