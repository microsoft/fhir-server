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
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ReindexJob (Id, Status, HeartbeatDateTime, RawJobRecord)
VALUES                     (@id, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;
GO
