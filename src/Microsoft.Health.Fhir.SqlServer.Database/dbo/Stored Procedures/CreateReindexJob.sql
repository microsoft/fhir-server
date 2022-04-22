CREATE PROCEDURE dbo.CreateReindexJob
@id VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ReindexJob (Id, Status, HeartbeatDateTime, RawJobRecord)
VALUES                     (@id, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;

