-- Our code generator does not understand views (in the end ReferenceSearchParam is a view), so we create a table that looks like a view and immediately drop it.
CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId            smallint     NOT NULL
   ,ResourceSurrogateId       bigint       NOT NULL
   ,SearchParamId             smallint     NOT NULL
   ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId   smallint     NULL
   ,ReferenceResourceIdInt    bigint       NOT NULL
   ,ReferenceResourceId       varchar(64)  COLLATE Latin1_General_100_CS_AS NOT NULL
)
GO
DROP TABLE dbo.ReferenceSearchParam
GO
CREATE TABLE dbo.ResourceReferenceSearchParams
(
    ResourceTypeId            smallint     NOT NULL
   ,ResourceSurrogateId       bigint       NOT NULL
   ,SearchParamId             smallint     NOT NULL
   ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId   smallint     NOT NULL
   ,ReferenceResourceIdInt    bigint       NOT NULL
   ,IsResourceRef             bit          NOT NULL CONSTRAINT DF_ResourceReferenceSearchParams_IsResourceRef DEFAULT 1, CONSTRAINT CH_ResourceReferenceSearchParams_IsResourceRef CHECK (IsResourceRef = 1)
)

ALTER TABLE dbo.ResourceReferenceSearchParams ADD CONSTRAINT FK_ResourceReferenceSearchParams_ReferenceResourceIdInt_ReferenceResourceTypeId_ResourceIdIntMap FOREIGN KEY (ReferenceResourceIdInt, ReferenceResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

ALTER TABLE dbo.ResourceReferenceSearchParams SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId_ResourceTypeId 
  ON dbo.ResourceReferenceSearchParams (ResourceSurrogateId, SearchParamId, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_ReferenceResourceIdInt_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId 
  ON dbo.ResourceReferenceSearchParams (ReferenceResourceIdInt, ReferenceResourceTypeId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
CREATE TABLE dbo.StringReferenceSearchParams
(
    ResourceTypeId            smallint     NOT NULL
   ,ResourceSurrogateId       bigint       NOT NULL
   ,SearchParamId             smallint     NOT NULL
   ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceId       varchar(64)  COLLATE Latin1_General_100_CS_AS NOT NULL
   ,IsResourceRef             bit          NOT NULL CONSTRAINT DF_StringReferenceSearchParams_IsResourceRef DEFAULT 0, CONSTRAINT CH_StringReferenceSearchParams_IsResourceRef CHECK (IsResourceRef = 0)
)

ALTER TABLE dbo.StringReferenceSearchParams SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId_ResourceTypeId 
  ON dbo.StringReferenceSearchParams (ResourceSurrogateId, SearchParamId, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_ReferenceResourceId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId 
  ON dbo.StringReferenceSearchParams (ReferenceResourceId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
