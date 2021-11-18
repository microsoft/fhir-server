/*************************************************************
    Stored procedures for get next available task
**************************************************************/
--
-- STORED PROCEDURE
--     GetNextTask
--
-- DESCRIPTION
--     Get next available tasks
--
-- PARAMETERS
--     @queueId
--         * The ID of the task record
--     @count
--         * Batch count for tasks list
--     @taskHeartbeatTimeoutThresholdInSeconds
--         * Timeout threshold in seconds for heart keep alive
CREATE PROCEDURE [dbo].[GetNextTask_2]
    @queueId varchar(64),
    @count smallint,
    @taskHeartbeatTimeoutThresholdInSeconds int = 600
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @expirationDateTime AS DATETIME2 (7);
SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
DECLARE @availableJobs TABLE (
    TaskId            VARCHAR (64) ,
    QueueId           VARCHAR (64) ,
    Status            SMALLINT     ,
    TaskTypeId        SMALLINT     ,
    IsCanceled        BIT          ,
    RetryCount        SMALLINT     ,
    HeartbeatDateTime DATETIME2    ,
    InputData         VARCHAR (MAX),
    TaskContext       VARCHAR (MAX),
    Result            VARCHAR (MAX));
INSERT INTO @availableJobs
SELECT   TOP (@count) TaskId,
                      QueueId,
                      Status,
                      TaskTypeId,
                      IsCanceled,
                      RetryCount,
                      HeartbeatDateTime,
                      InputData,
                      TaskContext,
                      Result
FROM     dbo.TaskInfo
WHERE    (QueueId = @queueId
          AND (Status = 1
               OR (Status = 2
                   AND HeartbeatDateTime <= @expirationDateTime)))
ORDER BY HeartbeatDateTime;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    Status            = 2,
       HeartbeatDateTime = @heartbeatDateTime,
       RunId             = CAST (NEWID() AS NVARCHAR (50))
FROM   dbo.TaskInfo AS task
       INNER JOIN
       @availableJobs AS availableJob
       ON task.TaskId = availableJob.TaskId;
SELECT task.TaskId,
       task.QueueId,
       task.Status,
       task.TaskTypeId,
       task.RunId,
       task.IsCanceled,
       task.RetryCount,
       task.MaxRetryCount,
       task.HeartbeatDateTime,
       task.InputData,
       task.TaskContext,
       task.Result
FROM   dbo.TaskInfo AS task
       INNER JOIN
       @availableJobs AS availableJob
       ON task.TaskId = availableJob.TaskId;
COMMIT TRANSACTION;
GO
