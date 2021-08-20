/*************************************************************
    Resource Bulk Import feature
**************************************************************/

IF TYPE_ID(N'BulkImportResourceType_1') IS NULL
BEGIN
    CREATE TYPE dbo.BulkImportResourceType_1 AS TABLE
    (
        ResourceTypeId smallint NOT NULL,
        ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
        Version int NOT NULL,
        IsHistory bit NOT NULL,
        ResourceSurrogateId bigint NOT NULL,
        IsDeleted bit NOT NULL,
        RequestMethod varchar(10) NULL,
        RawResource varbinary(max) NOT NULL,
        IsRawResourceMetaSet bit NOT NULL DEFAULT 0,
        SearchParamHash varchar(64) NULL
    )
END
GO 

/*************************************************************
    Stored procedures for batch delete resources
**************************************************************/
--
-- STORED PROCEDURE
--     BatchDeleteResources
--
-- DESCRIPTION
--     Batch delete resources
--
-- PARAMETERS
--     @resourceTypeId
--         * The resoruce type id
--     @startResourceSurrogateId
--         * The start ResourceSurrogateId
--     @endResourceSurrogateId
--         * The end ResourceSurrogateId
--     @batchSize
--         * Max batch size for delete operation
CREATE OR ALTER PROCEDURE dbo.BatchDeleteResources
    @resourceTypeId smallint,
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
AS
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DELETE Top(@batchSize) FROM dbo.Resource WITH (TABLOCK)
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    COMMIT TRANSACTION

    return @@rowcount
GO

/*************************************************************
    Stored procedures for batch delete ResourceWriteClaims
**************************************************************/
--
-- STORED PROCEDURE
--     BatchDeleteResourceWriteClaims
--
-- DESCRIPTION
--     Batch delete ResourceWriteClaims
--
-- PARAMETERS
--     @startResourceSurrogateId
--         * The start ResourceSurrogateId
--     @endResourceSurrogateId
--         * The end ResourceSurrogateId
--     @batchSize
--         * Max batch size for delete operation
CREATE OR ALTER PROCEDURE dbo.BatchDeleteResourceWriteClaims
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
AS
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DELETE Top(@batchSize) FROM dbo.ResourceWriteClaim WITH (TABLOCK)
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    COMMIT TRANSACTION

    return @@rowcount
GO


/*************************************************************
    Stored procedures for batch delete ResourceParams
**************************************************************/
--
-- STORED PROCEDURE
--     BatchDeleteResourceParams
--
-- DESCRIPTION
--     Batch delete ResourceParams
--
-- PARAMETERS
--     @tableName
--         * Resource params table name
--     @resourceTypeId
--         * Resource type id
--     @startResourceSurrogateId
--         * The start ResourceSurrogateId
--     @endResourceSurrogateId
--         * The end ResourceSurrogateId
--     @batchSize
--         * Max batch size for delete operation
CREATE OR ALTER PROCEDURE dbo.BatchDeleteResourceParams
    @tableName nvarchar(128),
    @resourceTypeId smallint,
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
AS
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @ParmDefinition NVARCHAR(512);

    IF OBJECT_ID(@tableName) IS NOT NULL BEGIN
        SET @sql = N'DELETE TOP(@BatchSizeParam) FROM ' + @tableName + N' WITH (TABLOCK) WHERE ResourceTypeId = @ResourceTypeIdParam AND ResourceSurrogateId >= @StartResourceSurrogateIdParam AND ResourceSurrogateId < @EndResourceSurrogateIdParam'
        SET @parmDefinition = N'@BatchSizeParam int, @ResourceTypeIdParam smallint, @StartResourceSurrogateIdParam bigint, @EndResourceSurrogateIdParam bigint'; 

        EXECUTE sp_executesql @sql, @parmDefinition,
                                @BatchSizeParam = @batchSize,
                                @ResourceTypeIdParam = @resourceTypeId,
                                @StartResourceSurrogateIdParam = @startResourceSurrogateId,
                                @EndResourceSurrogateIdParam = @endResourceSurrogateId
    END

    COMMIT TRANSACTION

    return @@rowcount
GO

/*************************************************************
    Stored procedures for disable index
**************************************************************/
--
-- STORED PROCEDURE
--     DisableIndex
--
-- DESCRIPTION
--     Stored procedures for disable index
--
-- PARAMETERS
--     @tableName
--         * index table name
--     @indexName
--         * index name
CREATE OR ALTER PROCEDURE [dbo].[DisableIndex]
    @tableName nvarchar(128),
    @indexName nvarchar(128)
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    DECLARE @IsExecuted INT
    SET @IsExecuted = 0 
    
    BEGIN TRANSACTION

    IF EXISTS
    (
        SELECT *
        FROM [sys].[indexes]
        WHERE name = @indexName
        AND object_id = OBJECT_ID(@tableName)
        AND is_disabled = 0
    )
    BEGIN
        DECLARE @Sql NVARCHAR(MAX);

        SET @Sql = N'ALTER INDEX ' +  QUOTENAME(@indexName)
        + N' on ' + @tableName + ' Disable'

        EXECUTE sp_executesql @Sql

        SET @IsExecuted = 1
    END

    COMMIT TRANSACTION

    RETURN @IsExecuted
GO

/*************************************************************
    Stored procedures for rebuild index
**************************************************************/
--
-- STORED PROCEDURE
--     RebuildIndex
--
-- DESCRIPTION
--     Stored procedures for rebuild index
--
-- PARAMETERS
--     @tableName
--         * index table name
--     @indexName
--         * index name
CREATE OR ALTER PROCEDURE [dbo].[RebuildIndex]
    @tableName nvarchar(128),
    @indexName nvarchar(128)
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    DECLARE @IsExecuted INT
    SET @IsExecuted = 0

    BEGIN TRANSACTION

    IF EXISTS
    (
        SELECT *
        FROM [sys].[indexes]
        WHERE name = @indexName
        AND object_id = OBJECT_ID(@tableName)
        AND is_disabled = 1
    )
    BEGIN
        DECLARE @Sql NVARCHAR(MAX);

        SET @Sql = N'ALTER INDEX ' +  QUOTENAME(@indexName)
        + N' on ' + @tableName + ' Rebuild'

        EXECUTE sp_executesql @Sql

        SET @IsExecuted = 1
    END

    COMMIT TRANSACTION

    RETURN @IsExecuted
GO

/*************************************************************
    Stored procedures for bulk merge resources
**************************************************************/
--
-- STORED PROCEDURE
--     BulkMergeResource
--
-- DESCRIPTION
--     Stored procedures for bulk merge resource
--
-- PARAMETERS
--     @resources
--         * input resources
CREATE OR ALTER PROCEDURE dbo.BulkMergeResource
    @resources dbo.BulkImportResourceType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    BEGIN TRANSACTION

    MERGE INTO [dbo].[Resource] WITH (ROWLOCK, INDEX(IX_Resource_ResourceTypeId_ResourceId_Version)) AS target
    USING @resources AS source
    ON source.[ResourceTypeId] = target.[ResourceTypeId]
        AND source.[ResourceId] = target.[ResourceId]
        AND source.[Version] = target.[Version]
    WHEN NOT MATCHED BY target THEN
    INSERT ([ResourceTypeId]
            , [ResourceId]
            , [Version]
            , [IsHistory]
            , [ResourceSurrogateId]
            , [IsDeleted]
            , [RequestMethod]
            , [RawResource]
            , [IsRawResourceMetaSet]
            , [SearchParamHash])
    VALUES ([ResourceTypeId]
            , [ResourceId]
            , [Version]
            , [IsHistory]
            , [ResourceSurrogateId]
            , [IsDeleted]
            , [RequestMethod]
            , [RawResource]
            , [IsRawResourceMetaSet]
            , [SearchParamHash])
    OUTPUT Inserted.[ResourceSurrogateId];

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for general task
**************************************************************/
--
-- STORED PROCEDURE
--     CreateTask_2
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
--     @maxRetryCount
--         * The maximum number for retry operation
--     @inputData
--         * Input data payload for the task
--     @isUniqueTaskByType
--         * Only create task if there's no other active task with same task type id
--
CREATE OR ALTER PROCEDURE [dbo].[CreateTask_2]
    @taskId varchar(64),
    @queueId varchar(64),
    @taskTypeId smallint,
    @maxRetryCount smallint = 3,
    @inputData varchar(max),
    @isUniqueTaskByType bit
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()
    DECLARE @status smallint = 1
    DECLARE @retryCount smallint = 0
    DECLARE @isCanceled bit = 0

    -- Check if the task already be created
    IF (@isUniqueTaskByType = 1) BEGIN
        IF EXISTS
        (
            SELECT *
            FROM [dbo].[TaskInfo]
            WHERE TaskId = @taskId or (TaskTypeId = @taskTypeId and Status <> 3)
        ) 
        BEGIN
            THROW 50409, 'Task already existed', 1;
        END
    END 
    ELSE BEGIN
        IF EXISTS
        (
            SELECT *
            FROM [dbo].[TaskInfo]
            WHERE TaskId = @taskId
        ) 
        BEGIN
            THROW 50409, 'Task already existed', 1;
        END
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
CREATE OR ALTER PROCEDURE [dbo].[GetNextTask_2]
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
