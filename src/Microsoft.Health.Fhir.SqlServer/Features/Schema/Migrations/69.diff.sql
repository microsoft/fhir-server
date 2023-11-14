
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam') AND name = 'IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId')
BEGIN
    CREATE UNIQUE INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId ON dbo.ReferenceSearchParam 
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
END

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam') AND name = 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion')
BEGIN
  DROP INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion ON dbo.ReferenceSearchParam
END
