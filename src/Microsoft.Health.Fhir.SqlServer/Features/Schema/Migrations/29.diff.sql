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
IF NOT EXISTS (SELECT 'X' FROM SYS.INDEXES WHERE name = 'IX_Status_QueueId' AND OBJECT_ID = OBJECT_ID('TaskInfo'))
BEGIN
CREATE NONCLUSTERED INDEX IX_Status_QueueId ON dbo.TaskInfo
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
CREATE OR ALTER PROCEDURE [dbo].[GetNextTask_3]
@queueId VARCHAR (64), @taskHeartbeatTimeoutThresholdInSeconds INT=600
AS

SET NOCOUNT ON;
DECLARE @lock VARCHAR(200) = 'GetNextTask_Q='+@queueId
        ,@taskId VARCHAR (64) = NULL
        ,@expirationDateTime AS DATETIME2 (7)
        ,@heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
 
BEGIN TRY
    BEGIN TRANSACTION

    EXECUTE sp_getapplock @lock, 'Exclusive'

-- try new tasks first
    UPDATE T
      SET Status = 2 -- running
         ,StartDateTime = SYSUTCDATETIME()
         ,HeartbeatDateTime = SYSUTCDATETIME()
         ,Worker = host_name()
         ,RunId = CAST (NEWID() AS NVARCHAR (50)) 
         ,@taskId = T.TaskId
      FROM dbo.TaskInfo T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        TaskId
                   FROM dbo.TaskInfo WITH (INDEX = IX_Status_QueueId)
                   WHERE QueueId = @queueId
                     AND Status = 1 -- Created
                   ORDER BY 
                        TaskId
                ) S
             ON T.QueueId = @queueId AND T.TaskId = S.TaskId 

  IF @taskId IS NULL
  -- old ones now
    UPDATE T
      SET StartDateTime = SYSUTCDATETIME()
        ,HeartbeatDateTime = SYSUTCDATETIME()
        ,Worker = HOST_NAME()
        ,RunId = CAST (NEWID() AS NVARCHAR (50))
        ,@taskId = T.TaskId
        ,RestartInfo = ISNULL(RestartInfo,'')+' Prev: Worker='+Worker+' Start='+convert(varchar,SYSUTCDATETIME(),121) 
      FROM dbo.TaskInfo T WITH (PAGLOCK)
          JOIN (SELECT TOP 1 
                        TaskId
                  FROM dbo.TaskInfo WITH (INDEX = IX_Status_QueueId)
                  WHERE QueueId = @queueId
                    AND Status = 2 -- running
                    AND HeartbeatDateTime <= @expirationDateTime
                  ORDER BY 
                        TaskId
                ) S
            ON T.QueueId = @queueId AND T.TaskId = S.TaskId 

  COMMIT TRANSACTION

  EXECUTE dbo.GetTaskDetails @TaskId = @taskId
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  THROW
END CATCH

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
CREATE OR ALTER PROCEDURE [dbo].[ResetTask_2]
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
CREATE PROCEDURE [dbo].[CompleteTask]
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
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    Status            = 3,
       HeartbeatDateTime = @heartbeatDateTime,
       EndDateTime       = @heartbeatDateTime,
       Result            = @taskResult
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