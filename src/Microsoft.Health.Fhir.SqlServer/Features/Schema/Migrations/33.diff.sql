/*************************************************************
    TaskInfo table
**************************************************************/

IF NOT EXISTS (SELECT 'X' FROM SYS.COLUMNS WHERE OBJECT_ID = OBJECT_ID(N'TaskInfo') AND NAME = 'ParentTaskId')
BEGIN
ALTER TABLE dbo.TaskInfo
ADD
    ParentTaskId VARCHAR (64) NULL
END
GO

/*************************************************************
    QueueId and status combined Index 
**************************************************************/
IF NOT EXISTS (SELECT 'X' FROM SYS.INDEXES WHERE name = 'IX_QueueId_ParentTaskId' AND OBJECT_ID = OBJECT_ID('TaskInfo'))
BEGIN
CREATE NONCLUSTERED INDEX IX_QueueId_ParentTaskId ON dbo.TaskInfo
(
    QueueId,
    ParentTaskId
)
END
GO


/*************************************************************
    Stored procedures for general task
**************************************************************/
--
-- STORED PROCEDURE
--     CreateTask_3
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
--     @parentTaskId
--         * The ID of the parent task
--     @maxRetryCount
--         * The maximum number for retry operation
--     @inputData
--         * Input data payload for the task
--     @isUniqueTaskByType
--         * Only create task if there's no other active task with same task type id
--

GO
CREATE OR ALTER PROCEDURE [dbo].[CreateTask_3]
@taskId VARCHAR (64), @queueId VARCHAR (64), @taskTypeId SMALLINT, @parentTaskId VARCHAR (64), @maxRetryCount SMALLINT=3, @inputData VARCHAR (MAX), @isUniqueTaskByType BIT
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
INSERT  INTO [dbo].[TaskInfo] (TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, ParentTaskId)
VALUES                       (@taskId, @queueId, @status, @taskTypeId, @isCanceled, @retryCount, @maxRetryCount, @heartbeatDateTime, @inputData, @parentTaskId);

EXECUTE dbo.GetTaskDetails @TaskId = @taskId

COMMIT TRANSACTION;
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

GO
CREATE OR ALTER PROCEDURE [dbo].[GetTaskDetails]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result, ParentTaskId
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId
GO

CREATE OR ALTER PROCEDURE [dbo].[GetImportProcessingTaskResult]
    @queueId VARCHAR (64),
    @importTaskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT Result
	FROM [dbo].[TaskInfo] WITH (INDEX (IX_QueueId_ParentTaskId))
	where ParentTaskId = @importTaskId and TaskTypeId = 1 and Status = 3
GO
