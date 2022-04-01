/*************************************************************
    TaskInfo table
**************************************************************/

IF NOT EXISTS (SELECT 'X' FROM SYS.COLUMNS WHERE OBJECT_ID = OBJECT_ID(N'TaskInfo') AND NAME = 'CreateDateTime')
BEGIN
ALTER TABLE dbo.TaskInfo
ADD
    CreateDateTime DATETIME2 (7) NOT NULL,
    StartDateTime DATETIME2 (7) NULL,
    EndDateTime DATETIME2 (7) NULL,
    Worker varchar(100) NULL,
    RestartInfo varchar(max) NULL,
    CONSTRAINT DF_TaskInfo_CreateDate DEFAULT SYSUTCDATETIME() FOR CreateDateTime
END
GO

/*************************************************************
    QueueId and status combined Index 
**************************************************************/
IF NOT EXISTS (SELECT 'X' FROM SYS.INDEXES WHERE name = 'IX_QueueId_Status' AND OBJECT_ID = OBJECT_ID('TaskInfo'))
BEGIN
CREATE NONCLUSTERED INDEX IX_QueueId_Status ON dbo.TaskInfo
(
    QueueId,
    Status
)
END
GO

/*************************************************************
    Stored procedures for get next available task
**************************************************************/
--
-- STORED PROCEDURE
--     GetNextTask
--
-- DESCRIPTION
--     Get next available task
--
-- PARAMETERS
--     @queueId
--         * The ID of the task record
--     @taskHeartbeatTimeoutThresholdInSeconds
--         * Timeout threshold in seconds for heart keep alive

GO
CREATE OR ALTER PROCEDURE dbo.GetNextTask_3
@queueId VARCHAR (64), @taskHeartbeatTimeoutThresholdInSeconds INT=600
AS
SET NOCOUNT ON;
DECLARE @lock AS VARCHAR (200) = 'GetNextTask_Q=' + @queueId, @taskId AS VARCHAR (64) = NULL, @expirationDateTime AS DATETIME2 (7), @startDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
SET @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, @startDateTime);
BEGIN TRY
    BEGIN TRANSACTION;
    EXECUTE sp_getapplock @lock, 'Exclusive';
    UPDATE T
    SET    Status            = 2,
           StartDateTime     = @startDateTime,
           HeartbeatDateTime = @startDateTime,
           Worker            = host_name(),
           RunId             = NEWID(),
           @taskId           = T.TaskId
    FROM   dbo.TaskInfo AS T WITH (PAGLOCK)
           INNER JOIN
           (SELECT   TOP 1 TaskId
            FROM     dbo.TaskInfo WITH (INDEX (IX_QueueId_Status))
            WHERE    QueueId = @queueId
                     AND Status = 1
            ORDER BY TaskId) AS S
           ON T.QueueId = @queueId
              AND T.TaskId = S.TaskId;
    IF @taskId IS NULL
        UPDATE T
        SET    StartDateTime     = @startDateTime,
               HeartbeatDateTime = @startDateTime,
               Worker            = host_name(),
               RunId             = NEWID(),
               @taskId           = T.TaskId,
               RestartInfo       = ISNULL(RestartInfo, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, @startDateTime, 121)
        FROM   dbo.TaskInfo AS T WITH (PAGLOCK)
               INNER JOIN
               (SELECT   TOP 1 TaskId
                FROM     dbo.TaskInfo WITH (INDEX (IX_QueueId_Status))
                WHERE    QueueId = @queueId
                         AND Status = 2
                         AND HeartbeatDateTime <= @expirationDateTime
                ORDER BY TaskId) AS S
               ON T.QueueId = @queueId
                  AND T.TaskId = S.TaskId;
    COMMIT TRANSACTION;
    EXECUTE dbo.GetTaskDetails @TaskId = @taskId;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK TRANSACTION THROW;
END CATCH
GO

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
CREATE OR ALTER PROCEDURE dbo.ResetTask_2
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
CREATE OR ALTER PROCEDURE dbo.CompleteTask
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
SET    Status            = 3,
       EndDateTime       = SYSUTCDATETIME(),
       Result            = @taskResult
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;
EXECUTE dbo.GetTaskDetails @TaskId = @taskId
GO
