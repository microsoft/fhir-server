/*************************************************************
    Stored procedures - HardDeleteResource_2
**************************************************************/
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
CREATE PROCEDURE dbo.HardDeleteResource_2
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @keepCurrentVersion smallint
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resourceSurrogateIds TABLE (
    ResourceSurrogateId BIGINT NOT NULL);
DELETE dbo.Resource
OUTPUT deleted.ResourceSurrogateId INTO @resourceSurrogateIds
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND NOT (@keepCurrentVersion = 1
                AND IsHistory = 0);
DELETE dbo.ResourceWriteClaim
WHERE  ResourceSurrogateId IN (SELECT ResourceSurrogateId
                               FROM   @resourceSurrogateIds);
DELETE dbo.CompartmentAssignment
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.ReferenceSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenText
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.StringSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.UriSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.NumberSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.QuantitySearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.DateTimeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.ReferenceTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenDateTimeCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenQuantityCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenStringCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenNumberNumberCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
COMMIT TRANSACTION;
