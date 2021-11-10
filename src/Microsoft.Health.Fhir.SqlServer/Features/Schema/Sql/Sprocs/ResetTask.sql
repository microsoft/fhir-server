/*************************************************************
    Stored procedures for reset task
**************************************************************/
--
-- STORED PROCEDURE
--     ResetTask
--
-- DESCRIPTION
--     Reset the task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[ResetTask]
    @taskId varchar(64),
    @runId varchar(50),
    @result varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    -- Can only reset task with same runid
    DECLARE @retryCount smallint
    DECLARE @status smallint
    DECLARE @maxRetryCount smallint

    SELECT @retryCount = RetryCount, @status = Status, @maxRetryCount = MaxRetryCount
    FROM [dbo].[TaskInfo]
    WHERE TaskId = @taskId and RunId = @runId

	-- We will timestamp the jobs when we update them to track stale jobs.
    IF (@retryCount IS NULL) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    IF (@retryCount >= @maxRetryCount) BEGIN
		UPDATE dbo.TaskInfo
		SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @result
		WHERE TaskId = @taskId
	END
    Else IF (@status <> 3) BEGIN
        UPDATE dbo.TaskInfo
		SET Status = 1, HeartbeatDateTime = @heartbeatDateTime, Result = @result, RetryCount = @retryCount + 1
		WHERE TaskId = @taskId
	END

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO
