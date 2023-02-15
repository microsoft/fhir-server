-- Adding a new index so that the reindex job can make use of getting counts in large dbs
GO
DROP INDEX IF EXISTS IX_Resource_ResourceTypeId_SearchParamHash ON dbo.Resource
GO

CREATE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_SearchParamHash
    ON dbo.Resource(ResourceTypeId ASC, SearchParamHash ASC);
GO
