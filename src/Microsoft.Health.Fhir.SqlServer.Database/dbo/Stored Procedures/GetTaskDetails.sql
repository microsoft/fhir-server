CREATE PROCEDURE [dbo].[GetTaskDetails]
@taskId VARCHAR (64)
AS
SET NOCOUNT ON;
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

