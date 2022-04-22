CREATE PROCEDURE dbo.CreateExportJob
@id VARCHAR (64), @hash VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ExportJob (Id, Hash, Status, HeartbeatDateTime, RawJobRecord)
VALUES                    (@id, @hash, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;

