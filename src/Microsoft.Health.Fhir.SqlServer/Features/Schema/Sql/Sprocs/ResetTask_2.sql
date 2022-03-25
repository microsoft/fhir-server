
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
--     @result
--         * The result of the task
--
GO
CREATE PROCEDURE dbo.ResetTask_2
@taskId VARCHAR (64), @runId VARCHAR (50), @result VARCHAR (MAX)
AS
SET NOCOUNT ON;
DECLARE @retryCount AS SMALLINT = NULL;
BEGIN TRY
    BEGIN TRANSACTION;
        UPDATE  dbo.TaskInfo
        SET     Status            = 3,
                HeartbeatDateTime = SYSUTCDATETIME(),
                Result            = @result,
                @retryCount = retryCount
        WHERE   TaskId = @taskId
                AND RunId = @runId
                AND (MaxRetryCount <> -1 AND RetryCount >= MaxRetryCount)

        IF @retryCount IS NULL
            UPDATE  dbo.TaskInfo
            SET     Status            = 1,
                    HeartbeatDateTime = SYSUTCDATETIME(),
                    Result            = @result,
                    RetryCount        = RetryCount + 1
            WHERE   TaskId = @taskId
                    AND RunId = @runId
                    AND Status <> 3
                    AND (MaxRetryCount = -1 OR RetryCount < MaxRetryCount)
    COMMIT TRANSACTION;
    EXECUTE dbo.GetTaskDetails @TaskId = @taskId
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK TRANSACTION THROW;
END CATCH
GO
