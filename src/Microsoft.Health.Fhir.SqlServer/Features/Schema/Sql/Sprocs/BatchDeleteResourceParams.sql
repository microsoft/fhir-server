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
CREATE PROCEDURE dbo.BatchDeleteResourceParams
    @tableName nvarchar(128),
    @resourceTypeId smallint,
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @Sql AS NVARCHAR (MAX);
DECLARE @ParmDefinition AS NVARCHAR (512);
IF OBJECT_ID(@tableName) IS NOT NULL
    BEGIN
        SET @sql = N'DELETE TOP(@BatchSizeParam) FROM ' + @tableName + N' WITH (TABLOCK) WHERE ResourceTypeId = @ResourceTypeIdParam AND ResourceSurrogateId >= @StartResourceSurrogateIdParam AND ResourceSurrogateId < @EndResourceSurrogateIdParam';
        SET @parmDefinition = N'@BatchSizeParam int, @ResourceTypeIdParam smallint, @StartResourceSurrogateIdParam bigint, @EndResourceSurrogateIdParam bigint';
        EXECUTE sp_executesql @sql, @parmDefinition, @BatchSizeParam = @batchSize, @ResourceTypeIdParam = @resourceTypeId, @StartResourceSurrogateIdParam = @startResourceSurrogateId, @EndResourceSurrogateIdParam = @endResourceSurrogateId;
    END
COMMIT TRANSACTION;
RETURN @@rowcount;
GO
