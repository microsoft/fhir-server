CREATE PROCEDURE dbo.UpdateExportJob
@id VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX), @jobVersion BINARY (8)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @currentJobVersion AS BINARY (8);
SELECT @currentJobVersion = JobVersion
FROM   dbo.ExportJob WITH (UPDLOCK, HOLDLOCK)
WHERE  Id = @id;
IF (@currentJobVersion IS NULL)
    BEGIN
        THROW 50404, 'Export job record not found', 1;
    END
IF (@jobVersion <> @currentJobVersion)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.ExportJob
SET    Status            = @status,
       HeartbeatDateTime = @heartbeatDateTime,
       RawJobRecord      = @rawJobRecord
WHERE  Id = @id;
SELECT @@DBTS;
COMMIT TRANSACTION;

