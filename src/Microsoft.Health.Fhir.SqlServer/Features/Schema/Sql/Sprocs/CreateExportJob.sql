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
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ExportJob (Id, Hash, Status, HeartbeatDateTime, RawJobRecord)
VALUES                    (@id, @hash, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;
GO
