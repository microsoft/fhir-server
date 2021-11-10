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
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())

    DECLARE @availableJobs TABLE (
        TaskId varchar(64),
        QueueId varchar(64),
        Status smallint,
        TaskTypeId smallint,
        IsCanceled bit,
        RetryCount smallint,
        HeartbeatDateTime datetime2,
        InputData varchar(max),
        TaskContext varchar(max),
        Result varchar(max)
    )

    INSERT INTO @availableJobs
    SELECT TOP(@count) TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, HeartbeatDateTime, InputData, TaskContext, Result
    FROM dbo.TaskInfo
    WHERE (QueueId = @queueId AND (Status = 1 OR (Status = 2 AND HeartbeatDateTime <= @expirationDateTime)))
    ORDER BY HeartbeatDateTime

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.TaskInfo
    SET Status = 2, HeartbeatDateTime = @heartbeatDateTime, RunId = CAST(NEWID() AS NVARCHAR(50))
    FROM dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

    Select task.TaskId, task.QueueId, task.Status, task.TaskTypeId, task.RunId, task.IsCanceled, task.RetryCount, task.MaxRetryCount, task.HeartbeatDateTime, task.InputData, task.TaskContext, task.Result
    from dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

    COMMIT TRANSACTION
GO
