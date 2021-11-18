/*************************************************************
    Stored procedures for update task context
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateTaskContext
--
-- DESCRIPTION
--     Update task context.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskContext
--         * The context of the task
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[UpdateTaskContext]
    @taskId varchar(64),
    @taskContext varchar(max),
    @runId varchar(50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Can only update task context with same runid
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

-- We will timestamp the jobs when we update them to track stale jobs.
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    HeartbeatDateTime = @heartbeatDateTime,
       TaskContext       = @taskContext
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
GO
