-- Adding a new index so that the reindex job can make use of getting counts in large dbs
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Resource_ResourceTypeId_SearchParamHash' AND object_id = object_id('Resource')) BEGIN
	CREATE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_SearchParamHash
		ON dbo.Resource(ResourceTypeId ASC, SearchParamHash ASC);
END
GO
