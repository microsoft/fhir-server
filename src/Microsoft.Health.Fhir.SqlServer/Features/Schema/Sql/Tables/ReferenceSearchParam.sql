CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId            smallint     NOT NULL
   ,ResourceSurrogateId       bigint       NOT NULL
   ,SearchParamId             smallint     NOT NULL
   ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId   smallint     NOT NULL
   ,ReferenceResourceIdInt    bigint       NOT NULL CONSTRAINT DF_ReferenceSearchParam_ResourceIdInt DEFAULT 0
   ,ReferenceResourceId       varchar(64)  COLLATE Latin1_General_100_CS_AS NOT NULL CONSTRAINT DF_ReferenceSearchParam_ResourceId DEFAULT ''
   ,ReferenceResourceVersion  int          NULL

   ,CONSTRAINT CH_ReferenceSearchParam_ReferenceResourceIdInt_ReferenceResourceId CHECK (ReferenceResourceIdInt = 0 AND ReferenceResourceId <> '' OR ReferenceResourceIdInt <> 0 AND ReferenceResourceId = '')
)

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId_ResourceTypeId
  ON dbo.ReferenceSearchParam (ResourceSurrogateId, SearchParamId, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_ReferenceResourceIdInt_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId ON dbo.ReferenceSearchParam 
  ( 
    ReferenceResourceIdInt
   ,ReferenceResourceId
   ,ReferenceResourceTypeId
   ,SearchParamId
   ,BaseUri
   ,ResourceSurrogateId
   ,ResourceTypeId
  )
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)



