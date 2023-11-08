CREATE INDEX IX_ReferenceResourceId_SearchParamId_BaseUri_ResourceSurrogateId ON dbo.ReferenceSearchParam (ReferenceResourceId,SearchParamId,BaseUri,ResourceSurrogateId)
  WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

DROP INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion ON dbo.ReferenceSearchParam

ALTER TABLE dbo.ReferenceSearchParam ALTER COLUMN ReferenceResourceTypeId smallint NOT NULL

ALTER TABLE dbo.ReferenceSearchParam ADD CONSTRAINT CH_ReferenceSearchParam_IsHistory CHECK (IsHistory = 0)

ALTER TABLE dbo.ReferenceSearchParam ADD CONSTRAINT PK_ReferenceSearchParam_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId PRIMARY KEY NONCLUSTERED 
  ( 
    ReferenceResourceId
   ,ReferenceResourceTypeId
   ,SearchParamId
   ,BaseUri
   ,ResourceSurrogateId
   ,ResourceTypeId
  )
  WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
