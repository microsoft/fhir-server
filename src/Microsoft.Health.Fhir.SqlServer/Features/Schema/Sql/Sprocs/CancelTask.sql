/*************************************************************
    Stored procedures for cancel task
**************************************************************/
--
-- STORED PROCEDURE
--     CancelTask
--
-- DESCRIPTION
--     Cancel the task and update task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE PROCEDURE [dbo].[CancelTask]
    @taskId varchar(64)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- We will timestamp the jobs when we update them to track stale jobs.
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId)
    BEGIN
        THROW 50404, 'Task not exist', 1;
    END
UPDATE dbo.TaskInfo
SET    IsCanceled        = 1,
       HeartbeatDateTime = @heartbeatDateTime
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
