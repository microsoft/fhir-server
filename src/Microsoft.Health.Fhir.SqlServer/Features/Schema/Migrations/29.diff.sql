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
DECLARE @expirationDateTime AS DATETIME2 (7);
SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
DECLARE @availableJobs TABLE (
    TaskId            VARCHAR (64)
	);

BEGIN TRY
    BEGIN TRANSACTION

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    INSERT INTO @availableJobs
    SELECT TOP (@count) TaskId
    FROM dbo.TaskInfo WITH (INDEX = IX_Status)
    WHERE QueueId = @QueueId
        AND Status = 2 
        AND HeartbeatDateTime <= @expirationDateTime
    ORDER BY 
        TaskId

	SET @count = (SELECT @count - (SELECT COUNT(*) FROM  @availableJobs) )

	INSERT INTO @availableJobs
	SELECT TOP (@count) TaskId
	FROM dbo.TaskInfo WITH (INDEX = IX_Status)
	WHERE QueueId = @QueueId
		AND Status = 1 -- Created
	ORDER BY 
		TaskId

	UPDATE dbo.TaskInfo
		SET    Status            = 2,
		HeartbeatDateTime = @heartbeatDateTime,
		RunId             = CAST (NEWID() AS NVARCHAR (50))
		FROM dbo.TaskInfo T WITH (PAGLOCK)
		JOIN @availableJobs AS J
       ON T.TaskId = J.TaskId;
  COMMIT TRANSACTION

SELECT T.TaskId,
       T.QueueId,
       T.Status,
       T.TaskTypeId,
       T.RunId,
       T.IsCanceled,
       T.RetryCount,
       T.MaxRetryCount,
       T.HeartbeatDateTime,
       T.InputData,
       T.TaskContext,
       T.Result
FROM   dbo.TaskInfo AS T
       INNER JOIN
       @availableJobs AS J
       ON T.TaskId = J.TaskId;

END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  THROW
END CATCH

GO
