/*************************************************************
    Reindex Job
**************************************************************/
CREATE TABLE dbo.ReindexJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ReindexJob ON dbo.ReindexJob
(
    Id
)

GO

/*************************************************************
    Stored procedures for reindexing
**************************************************************/
--
-- STORED PROCEDURE
--     Creates an reindex job.
--
-- DESCRIPTION
--     Creates a new row to the ReindexJob table, adding a new job to the queue of jobs to be processed.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--
-- RETURN VALUE
--     The row version of the created reindex job.
--
CREATE PROCEDURE dbo.CreateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    INSERT INTO dbo.ReindexJob
        (Id, Status, HeartbeatDateTime, RawJobRecord)
    VALUES
        (@id, @status, @heartbeatDateTime, @rawJobRecord)

    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets an reindex job given its ID.
--
-- DESCRIPTION
--     Retrieves the reindex job record from the ReindexJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record to retrieve
--
-- RETURN VALUE
--     The matching reindex job.
--
CREATE PROCEDURE dbo.GetReindexJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ReindexJob
    WHERE Id = @id
GO

--
-- STORED PROCEDURE
--     Updates a reindex job.
--
-- DESCRIPTION
--     Modifies an existing job in the ReindexJob table.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--     @jobVersion
--         * The version of the job to update must match this
--
-- RETURN VALUE
--     The row version of the updated reindex job.
--
CREATE PROCEDURE dbo.UpdateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max),
    @jobVersion binary(8)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @currentJobVersion binary(8)

    -- Acquire and hold an update lock on a row in the ReindexJob table for the entire transaction.
    -- This ensures the version check and update occur atomically.
    SELECT @currentJobVersion = JobVersion
    FROM dbo.ReindexJob WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @id

    IF (@currentJobVersion IS NULL) BEGIN
        THROW 50404, 'Reindex job record not found', 1;
    END

    IF (@jobVersion <> @currentJobVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.ReindexJob
    SET Status = @status, HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = @rawJobRecord
    WHERE Id = @id

    SELECT MIN_ACTIVE_ROWVERSION()

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Acquires reindex jobs.
--
-- DESCRIPTION
--     Timestamps the available reindex jobs and sets their statuses to running.
--
-- PARAMETERS
--     @jobHeartbeatTimeoutThresholdInSeconds
--         * The number of seconds that must pass before a reindex job is considered stale
--     @maximumNumberOfConcurrentJobsAllowed
--         * The maximum number of running jobs we can have at once
--
-- RETURN VALUE
--     The updated jobs that are now running.
--
CREATE PROCEDURE dbo.AcquireReindexJobs
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
    SELECT @numberOfRunningJobs = COUNT(*) FROM dbo.ReindexJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > @expirationDateTime

    -- Determine how many available jobs we can pick up.
    DECLARE @limit int = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;

    IF (@limit > 0) BEGIN

        DECLARE @availableJobs TABLE (Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL, JobVersion binary(8) NOT NULL)

        -- Get the available jobs, which are reindex jobs that are queued or stale.
        -- Older jobs will be prioritized over newer ones.
        INSERT INTO @availableJobs
        SELECT TOP(@limit) Id, JobVersion
        FROM dbo.ReindexJob
        WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime

        DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

        -- Update each available job's status to running both in the reindex table's status column and in the raw reindex job record JSON.
        UPDATE dbo.ReindexJob
        SET Status = 'Running', HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = JSON_MODIFY(RawJobRecord,'$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM dbo.ReindexJob job INNER JOIN @availableJobs availableJob ON job.Id = availableJob.Id AND job.JobVersion = availableJob.JobVersion

    END

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Checks if there are any active reindex jobs.
--
-- DESCRIPTION
--     Queries the datastore for any reindex job documents with a status of running, queued or paused.
--
-- RETURN VALUE
--     The job IDs of any active reindex jobs.
--
CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
    SET NOCOUNT ON

    SELECT Id
    FROM dbo.ReindexJob
    WHERE Status = 'Running' OR Status = 'Queued' OR Status = 'Paused'
GO
