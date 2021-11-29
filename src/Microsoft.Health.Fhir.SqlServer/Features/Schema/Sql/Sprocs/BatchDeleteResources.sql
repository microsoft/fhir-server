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
CREATE PROCEDURE dbo.BatchDeleteResources
    @resourceTypeId smallint,
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
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
GO
