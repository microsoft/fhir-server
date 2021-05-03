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
ALTER PROCEDURE dbo.UpdateExportJob
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

    SELECT @@DBTS

    COMMIT TRANSACTION
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
ALTER PROCEDURE dbo.UpdateReindexJob
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

    SELECT @@DBTS

    COMMIT TRANSACTION
GO
