CREATE PROCEDURE [dbo].[TaskKeepAlive]
@taskId VARCHAR (64), @runId VARCHAR (50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    HeartbeatDateTime = @heartbeatDateTime
WHERE  TaskId = @taskId;
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData,
       TaskContext,
       Result
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

