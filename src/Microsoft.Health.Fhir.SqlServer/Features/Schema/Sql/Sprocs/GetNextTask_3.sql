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
--
GO
CREATE PROCEDURE dbo.GetNextTask_3
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
