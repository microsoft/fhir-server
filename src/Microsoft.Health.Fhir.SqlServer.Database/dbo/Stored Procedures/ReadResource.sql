CREATE PROCEDURE dbo.ReadResource
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @version INT=NULL
AS
SET NOCOUNT ON;
IF (@version IS NULL)
    BEGIN
        SELECT ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceId = @resourceId
               AND IsHistory = 0;
    END
ELSE
    BEGIN
        SELECT ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceId = @resourceId
               AND Version = @version;
    END

