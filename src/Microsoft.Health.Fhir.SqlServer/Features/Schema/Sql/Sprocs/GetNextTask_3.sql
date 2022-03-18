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
DECLARE @Lock VARCHAR(200) = 'GetNextTask_Q='+@queueId
        ,@TaskId VARCHAR (64) = NULL
        ,@expirationDateTime AS DATETIME2 (7)
        ,@heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
 
BEGIN TRY
    BEGIN TRANSACTION

    EXECUTE sp_getapplock @Lock, 'Exclusive'

-- try new tasks first
    UPDATE T
      SET Status = 2 -- running
         ,StartDateTime = SYSUTCDATETIME()
         ,HeartbeatDateTime = SYSUTCDATETIME()
         ,Worker = host_name() 
         ,@TaskId = T.TaskId
      FROM dbo.TaskInfo T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        TaskId
                   FROM dbo.TaskInfo WITH (INDEX = IX_Status_QueueId)
                   WHERE QueueId = @QueueId
                     AND Status = 1 -- Created
                   ORDER BY 
                        TaskId
                ) S
             ON T.QueueId = @QueueId AND T.TaskId = S.TaskId 

  IF @TaskId IS NULL
  -- old ones now
    UPDATE T
      SET StartDateTime = SYSUTCDATETIME()
        ,HeartbeatDateTime = SYSUTCDATETIME()
        ,Worker = HOST_NAME() 
        ,@TaskId = T.TaskId
        ,RestartInfo = ISNULL(RestartInfo,'')+' Prev: Worker='+Worker+' Start='+convert(varchar,SYSUTCDATETIME(),121) 
      FROM dbo.TaskInfo T WITH (PAGLOCK)
          JOIN (SELECT TOP 1 
                        TaskId
                  FROM dbo.TaskInfo WITH (INDEX = IX_Status)
                  WHERE QueueId = @QueueId
                    AND Status = 2 -- running
                    AND HeartbeatDateTime <= @expirationDateTime
                  ORDER BY 
                        TaskId
                ) S
            ON T.QueueId = @QueueId AND T.TaskId = S.TaskId 

  COMMIT TRANSACTION

  EXECUTE dbo.GetTaskDetails @TaskId = @TaskId
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  THROW
END CATCH

GO

CREATE OR ALTER PROCEDURE [dbo].[CompleteTask]
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
       EndDateTime = SYSUTCDATETIME(),
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
