CREATE PROCEDURE dbo.HardDeleteResource_2
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @keepCurrentVersion SMALLINT
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

