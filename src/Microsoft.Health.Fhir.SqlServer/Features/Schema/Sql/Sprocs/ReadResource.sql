--
-- STORED PROCEDURE
--     ReadResource
--
-- DESCRIPTION
--     Reads a single resource, optionally a specific version of the resource.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID
--     @version
--         * A specific version of the resource. If null, returns the latest version.
-- RETURN VALUE
--         A result set with 0 or 1 rows.
--
CREATE PROCEDURE dbo.ReadResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @version int = NULL
AS
    SET NOCOUNT ON

    IF (@version IS NULL) BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version
    END
GO
