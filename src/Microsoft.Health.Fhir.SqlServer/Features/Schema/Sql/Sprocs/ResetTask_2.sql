
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
CREATE PROCEDURE [dbo].[ResetTask_2]
@taskId VARCHAR (64), @runId VARCHAR (50), @result VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @retryCount AS SMALLINT;
DECLARE @status AS SMALLINT;
DECLARE @maxRetryCount AS SMALLINT;
SELECT @retryCount = RetryCount,
       @status = Status,
       @maxRetryCount = MaxRetryCount
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId
       AND RunId = @runId;
IF (@retryCount IS NULL)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
IF (@maxRetryCount != -1 AND @retryCount > @maxRetryCount)  -- -1 means retry infinitely 
    BEGIN
        UPDATE dbo.TaskInfo
        SET    Status            = 3,
               HeartbeatDateTime = @heartbeatDateTime,
               Result            = @result
        WHERE  TaskId = @taskId;
    END
ELSE
    IF (@status <> 3)
        BEGIN
            UPDATE dbo.TaskInfo
            SET    Status            = 1,
                   HeartbeatDateTime = @heartbeatDateTime,
                   Result            = @result,
                   RetryCount        = @retryCount + 1
            WHERE  TaskId = @taskId;
        END
COMMIT TRANSACTION;

EXECUTE dbo.GetTaskDetails @TaskId = @taskId

GO
