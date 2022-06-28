/*************************************************************
    Stored procedures for complete task with result
**************************************************************/
--
-- STORED PROCEDURE
--     CompleteTask
--
-- DESCRIPTION
--     Complete the task and update task result.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskResult
--         * The result for the task execution
--     @runId
--         * Current runId for this exuction of the task
--
GO
CREATE PROCEDURE dbo.CompleteTask
    @taskId varchar(64),
    @taskResult varchar(max),
    @runId varchar(50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Can only complete task with same runid
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

UPDATE dbo.TaskInfo
SET    Status            = 3,
       EndDateTime       = SYSUTCDATETIME(),
       Result            = @taskResult
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

EXECUTE dbo.GetTaskDetails @TaskId = @taskId

GO
