--
-- STORED PROCEDURE
--     Deletes a single resource's history, and optionally the resource itself
--
-- DESCRIPTION
--     Permanently deletes all history data related to a resource. Optionally removes all data, including the current resource version.
--     Data remains recoverable from the transaction log, however.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as in the resource itself)
--     @keepCurrentVersion
--         * When 1, the current resource version kept, else all data is removed.
--
CREATE OR ALTER PROCEDURE dbo.HardDeleteResource_2
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @keepCurrentVersion smallint
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
BEGIN TRANSACTION

    DECLARE @resourceSurrogateIds TABLE(ResourceSurrogateId bigint NOT NULL)

    DELETE FROM dbo.Resource
        OUTPUT deleted.ResourceSurrogateId
    INTO @resourceSurrogateIds
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId
      AND NOT(@keepCurrentVersion=1 and IsHistory=0)

    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenText
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    COMMIT TRANSACTION
GO
