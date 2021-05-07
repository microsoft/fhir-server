
/*************************************************************
    Task Table
**************************************************************/
IF NOT EXISTS (SELECT 'X' FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TaskInfo')
BEGIN
    CREATE TABLE [dbo].[TaskInfo](
	    [TaskId] [varchar](64) NOT NULL,
	    [QueueId] [varchar](64) NOT NULL,
	    [Status] [smallint] NOT NULL,
        [TaskTypeId] [smallint] NOT NULL,
        [RunId] [varchar](50) null,
	    [IsCanceled] [bit] NOT NULL,
        [RetryCount] [smallint] NOT NULL,
        [MaxRetryCount] [smallint] NOT NULL,
	    [HeartbeatDateTime] [datetime2](7) NULL,
	    [InputData] [varchar](max) NOT NULL,
	    [TaskContext] [varchar](max) NULL,
        [Result] [varchar](max) NULL
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END 

IF NOT EXISTS (SELECT 'X' FROM SYS.INDEXES WHERE name = 'IXC_Task' AND OBJECT_ID = OBJECT_ID('TaskInfo'))
BEGIN
    CREATE UNIQUE CLUSTERED INDEX IXC_Task on [dbo].[TaskInfo]
    (
        TaskId
    )
END
GO

/*************************************************************
    Stored procedures for general task
**************************************************************/
--
-- STORED PROCEDURE
--     CreateTask
--
-- DESCRIPTION
--     Create task for given task payload.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record to create
--     @queueId
--         * The number of seconds that must pass before an export job is considered stale
--     @taskTypeId
--         * The maximum number of running jobs we can have at once
--
CREATE OR ALTER PROCEDURE [dbo].[CreateTask]
    @taskId varchar(64),
    @queueId varchar(64),
	@taskTypeId smallint,
    @maxRetryCount smallint = 3,
    @inputData varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()
	DECLARE @status smallint = 1
    DECLARE @retryCount smallint = 0
	DECLARE @isCanceled bit = 0

    -- Check if the task already be created
    IF EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId
    ) BEGIN
        THROW 50409, 'Task already existed', 1;
    END

    -- Create new task
    INSERT INTO [dbo].[TaskInfo]
        (TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData)
    VALUES
        (@taskId, @queueId, @status, @taskTypeId, @isCanceled, @retryCount, @maxRetryCount, @heartbeatDateTime, @inputData)

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for get task payload
**************************************************************/
--
-- STORED PROCEDURE
--     GetTaskDetails
--
-- DESCRIPTION
--     Get task payload.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE OR ALTER PROCEDURE [dbo].[GetTaskDetails]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId
GO


/*************************************************************
    Stored procedures for update task context
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateTaskContext
--
-- DESCRIPTION
--     Update task context.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskContext
--         * The context of the task
--     @runId
--         * Current runId for this exuction of the task
--
CREATE OR ALTER PROCEDURE [dbo].[UpdateTaskContext]
    @taskId varchar(64),
    @taskContext varchar(max),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only update task context with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET HeartbeatDateTime = @heartbeatDateTime, TaskContext = @taskContext
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO


/*************************************************************
    Stored procedures for keepalive task
**************************************************************/
--
-- STORED PROCEDURE
--     TaskKeepAlive
--
-- DESCRIPTION
--     Task keep-alive.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @runId
--         * Current runId for this exuction of the task
--
CREATE OR ALTER PROCEDURE [dbo].[TaskKeepAlive]
    @taskId varchar(64),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only update task context with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET HeartbeatDateTime = @heartbeatDateTime
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
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
CREATE OR ALTER PROCEDURE [dbo].[CompleteTask]
    @taskId varchar(64),
    @taskResult varchar(max),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only complete task with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @taskResult
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO


/*************************************************************
    Stored procedures for cancel task
**************************************************************/
--
-- STORED PROCEDURE
--     CancelTask
--
-- DESCRIPTION
--     Cancel the task and update task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE OR ALTER PROCEDURE [dbo].[CancelTask]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId
    ) BEGIN
        THROW 50404, 'Task not exist', 1;
    END

	UPDATE dbo.TaskInfo
	SET IsCanceled = 1, HeartbeatDateTime = @heartbeatDateTime
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
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
--
CREATE OR ALTER PROCEDURE [dbo].[ResetTask]
    @taskId varchar(64),
    @runId varchar(50),
    @result varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only reset task with same runid
    DECLARE @retryCount smallint
    DECLARE @status smallint
    DECLARE @maxRetryCount smallint

    SELECT @retryCount = RetryCount, @status = Status, @maxRetryCount = MaxRetryCount
    FROM [dbo].[TaskInfo]
    WHERE TaskId = @taskId and RunId = @runId

	-- We will timestamp the jobs when we update them to track stale jobs.
    IF (@retryCount IS NULL) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    IF (@retryCount >= @maxRetryCount) BEGIN
		UPDATE dbo.TaskInfo
		SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @result
		WHERE TaskId = @taskId
	END
    Else IF (@status <> 3) BEGIN
        UPDATE dbo.TaskInfo
		SET Status = 1, HeartbeatDateTime = @heartbeatDateTime, Result = @result, RetryCount = @retryCount + 1
		WHERE TaskId = @taskId
	END

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO

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
CREATE OR ALTER PROCEDURE [dbo].[GetNextTask]
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
    WHERE (QueueId = @queueId AND ((Status = 1 OR (Status = 2 AND HeartbeatDateTime <= @expirationDateTime)) AND IsCanceled = 0))
    ORDER BY HeartbeatDateTime

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.TaskInfo
    SET Status = 2, HeartbeatDateTime = @heartbeatDateTime, RunId = CAST(NEWID() AS NVARCHAR(50))
    FROM dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

	Select task.TaskId, task.QueueId, task.Status, task.TaskTypeId, task.RunId, task.IsCanceled, task.RetryCount, task.MaxRetryCount, task.HeartbeatDateTime, task.InputData, task.TaskContext, task.Result
	from dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

    COMMIT TRANSACTION
GO
