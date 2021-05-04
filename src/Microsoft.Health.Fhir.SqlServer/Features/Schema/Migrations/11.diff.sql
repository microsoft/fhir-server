IF TYPE_ID(N'IndexTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.IndexTableType_1 AS TABLE
(
    TableName nvarchar(128) COLLATE Latin1_General_CI_AI NOT NULL,
    IndexName nvarchar(128) COLLATE Latin1_General_CI_AI NOT NULL
)
END

GO

/*************************************************************
    Stored procedures for delete batch resources
**************************************************************/
--
-- STORED PROCEDURE
--     DeleteBatchResources
--
-- DESCRIPTION
--     Delete batch resources
--
-- PARAMETERS
--     @startResourceSurrogateId
--         * The start ResourceSurrogateId
--     @endResourceSurrogateId
--         * The end ResourceSurrogateId
CREATE OR ALTER PROCEDURE dbo.DeleteBatchResources
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DELETE FROM dbo.Resource
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenText
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceSurrogateId >= @startResourceSurrogateId AND ResourceSurrogateId < @endResourceSurrogateId
        
    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for disable indexes
**************************************************************/
--
-- STORED PROCEDURE
--     DisableIndexes
--
-- DESCRIPTION
--     Stored procedures for disable indexes
--
-- PARAMETERS
--     @indexes indexes table
CREATE OR ALTER PROCEDURE [dbo].[DisableIndexes]
    @indexes dbo.IndexTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    declare commands cursor for
    SELECT N'ALTER INDEX [' + indexes.IndexName + '] ON ' + indexes.TableName + ' Disable;'
    FROM @indexes as indexes

    declare @cmd varchar(max)

    open commands
    fetch next from commands into @cmd
    while @@FETCH_STATUS=0
    begin
      exec(@cmd)
      fetch next from commands into @cmd
    end

    select indexName, tableName from @indexes

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for rebuild indexes
**************************************************************/
--
-- STORED PROCEDURE
--     RebuildIndexes
--
-- DESCRIPTION
--     Stored procedures for rebuild indexes
--
-- PARAMETERS
--     @indexes indexes table
CREATE OR ALTER PROCEDURE [dbo].[RebuildIndexes]
    @indexes dbo.IndexTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    declare commands cursor for
    SELECT N'ALTER INDEX [' + indexes.IndexName + '] ON ' + indexes.TableName + ' Rebuild;'
    FROM @indexes as indexes

    declare @cmd varchar(max)

    open commands
    fetch next from commands into @cmd
    while @@FETCH_STATUS=0
    begin
      exec(@cmd)
      fetch next from commands into @cmd
    end

    select indexName, tableName from @indexes

    COMMIT TRANSACTION
GO
