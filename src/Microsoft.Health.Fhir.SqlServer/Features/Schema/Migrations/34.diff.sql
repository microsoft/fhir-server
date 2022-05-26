IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('Resource') AND name = 'IX_Resource_ResourceSurrogateId')
  DROP INDEX IX_Resource_ResourceSurrogateId ON dbo.Resource
GO
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = object_id('EventLog') AND name = 'ParentEventId')
  ALTER TABLE dbo.EventLog DROP COLUMN ParentEventId
GO
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = object_id('EventLog') AND name = 'TraceId')
  ALTER TABLE dbo.EventLog DROP COLUMN TraceId
GO
