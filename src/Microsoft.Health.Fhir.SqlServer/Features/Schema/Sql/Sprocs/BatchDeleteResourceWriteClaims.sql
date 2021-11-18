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
CREATE PROCEDURE dbo.BatchDeleteResourceWriteClaims
    @startResourceSurrogateId bigint,
    @endResourceSurrogateId bigint,
    @batchSize int
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DELETE TOP (@batchSize)
       dbo.ResourceWriteClaim WITH (TABLOCK)
WHERE  ResourceSurrogateId >= @startResourceSurrogateId
       AND ResourceSurrogateId < @endResourceSurrogateId;
COMMIT TRANSACTION;
RETURN @@rowcount;
GO
