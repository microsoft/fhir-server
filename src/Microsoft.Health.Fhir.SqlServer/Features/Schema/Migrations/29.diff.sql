
CREATE INDEX IX_Status ON dbo.TaskInfo (Status)
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
--     @count
--         * Batch count for tasks list
--     @taskHeartbeatTimeoutThresholdInSeconds
--         * Timeout threshold in seconds for heart keep alive

CREATE OR ALTER PROCEDURE [dbo].[GetNextTask_2]
@queueId VARCHAR (64), @count SMALLINT, @taskHeartbeatTimeoutThresholdInSeconds INT=600
AS
SET NOCOUNT ON;
DECLARE @Lock varchar(200) = 'GetNextTask_Q='+@queueId
       ,@TaskId int = NULL
DECLARE @expirationDateTime AS DATETIME2 (7);
SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();

BEGIN TRY
    BEGIN TRANSACTION

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    -- try old tasks first
    UPDATE dbo.TaskInfo
        SET     HeartbeatDateTime = @heartbeatDateTime,
                RunId             = CAST (NEWID() AS NVARCHAR (50)),
                @TaskId = T.TaskId
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

  IF @TaskId IS NULL
    -- new ones now
    UPDATE T
        SET     Status            = 2,
                HeartbeatDateTime = @heartbeatDateTime,
                RunId             = CAST (NEWID() AS NVARCHAR (50)),
                @TaskId = T.TaskId
        FROM dbo.TaskInfo T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        TaskId
                   FROM dbo.TaskInfo WITH (INDEX = IX_Status)
                   WHERE QueueId = @QueueId
                     AND Status = 1 -- Created
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