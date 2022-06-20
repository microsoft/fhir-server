
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
SET XACT_ABORT ON;
DECLARE @retryCount AS SMALLINT = NULL;
IF NOT EXISTS  (SELECT *
                FROM   dbo.TaskInfo
                WHERE  TaskId = @taskId
                       AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
UPDATE  dbo.TaskInfo
SET     Status            = 3,
        EndDateTime       = SYSUTCDATETIME(),
        Result            = @result,
        @retryCount = retryCount
WHERE   TaskId = @taskId
        AND RunId = @runId
        AND (MaxRetryCount <> -1 AND RetryCount >= MaxRetryCount)
IF @retryCount IS NULL
    UPDATE  dbo.TaskInfo
    SET     Status            = 1,
            Result            = @result,
            RetryCount        = RetryCount + 1,
            RestartInfo       = ISNULL(RestartInfo, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, StartDateTime, 121)
    WHERE   TaskId = @taskId
            AND RunId = @runId
            AND Status <> 3
            AND (MaxRetryCount = -1 OR RetryCount < MaxRetryCount)
EXECUTE dbo.GetTaskDetails @TaskId = @taskId
GO



