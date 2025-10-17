CREATE NONCLUSTERED INDEX IX_SearchParam_LastUpdated
ON dbo.SearchParam (LastUpdated)
WITH (DATA_COMPRESSION = PAGE)
GO
