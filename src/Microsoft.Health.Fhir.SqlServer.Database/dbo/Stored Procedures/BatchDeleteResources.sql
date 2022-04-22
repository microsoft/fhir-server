CREATE PROCEDURE dbo.BatchDeleteResources
@resourceTypeId SMALLINT, @startResourceSurrogateId BIGINT, @endResourceSurrogateId BIGINT, @batchSize INT
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DELETE TOP (@batchSize)
       dbo.Resource WITH (TABLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId >= @startResourceSurrogateId
       AND ResourceSurrogateId < @endResourceSurrogateId;
COMMIT TRANSACTION;
RETURN @@rowcount;

