IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('Resource') AND name = 'IX_Resource_ResourceSurrogateId')
  DROP INDEX IX_Resource_ResourceSurrogateId ON dbo.Resource
GO
