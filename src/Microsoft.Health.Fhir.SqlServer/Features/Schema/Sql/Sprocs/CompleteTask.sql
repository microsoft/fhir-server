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
CREATE PROCEDURE [dbo].[CompleteTask]
    @taskId varchar(64),
    @taskResult varchar(max),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    -- Can only complete task with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @taskResult
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO
