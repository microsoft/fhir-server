CREATE PROCEDURE dbo.BatchDeleteResourceParams
@tableName NVARCHAR (128), @resourceTypeId SMALLINT, @startResourceSurrogateId BIGINT, @endResourceSurrogateId BIGINT, @batchSize INT
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

