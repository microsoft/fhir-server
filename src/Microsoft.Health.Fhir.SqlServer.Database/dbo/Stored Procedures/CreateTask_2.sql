CREATE PROCEDURE [dbo].[CreateTask_2]
@taskId VARCHAR (64), @queueId VARCHAR (64), @taskTypeId SMALLINT, @maxRetryCount SMALLINT=3, @inputData VARCHAR (MAX), @isUniqueTaskByType BIT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
DECLARE @status AS SMALLINT = 1;
DECLARE @retryCount AS SMALLINT = 0;
DECLARE @isCanceled AS BIT = 0;
IF (@isUniqueTaskByType = 1)
    BEGIN
        IF EXISTS (SELECT *
                   FROM   [dbo].[TaskInfo]
                   WHERE  TaskId = @taskId
                          OR (TaskTypeId = @taskTypeId
                              AND Status <> 3))
            BEGIN
                THROW 50409, 'Task already existed', 1;
            END
    END
ELSE
    BEGIN
        IF EXISTS (SELECT *
                   FROM   [dbo].[TaskInfo]
                   WHERE  TaskId = @taskId)
            BEGIN
                THROW 50409, 'Task already existed', 1;
            END
    END
INSERT  INTO [dbo].[TaskInfo] (TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData)
VALUES                       (@taskId, @queueId, @status, @taskTypeId, @isCanceled, @retryCount, @maxRetryCount, @heartbeatDateTime, @inputData);
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

