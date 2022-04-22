CREATE PROCEDURE dbo.CompleteTask
@taskId VARCHAR (64), @taskResult VARCHAR (MAX), @runId VARCHAR (50)
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
UPDATE dbo.TaskInfo
SET    Status      = 3,
       EndDateTime = SYSUTCDATETIME(),
       Result      = @taskResult
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;
EXECUTE dbo.GetTaskDetails @TaskId = @taskId;

