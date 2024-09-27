CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL CONSTRAINT DF_Resource_ResourceId DEFAULT ''
   ,ResourceIdInt               bigint                  NOT NULL CONSTRAINT DF_Resource_ResourceIdInt DEFAULT 0
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL     -- used for main CRUD operation 
   ,HistoryTransactionId        bigint                  NULL     -- used by CRUD operation that moved resource version in invisible state 
   ,OffsetInFile                int                     NULL

    CONSTRAINT PKC_Resource_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId)
   ,CONSTRAINT CH_Resource_RawResource_OffsetInFile CHECK (RawResource IS NOT NULL OR OffsetInFile IS NOT NULL)
   ,CONSTRAINT CH_Resource_ResourceIdInt_ResourceId CHECK (ResourceIdInt = 0 AND ResourceId <> '' OR ResourceIdInt <> 0 AND ResourceId = '')
)

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_TransactionId_ResourceTypeId_WHERE_TransactionId_NOT_NULL ON dbo.Resource (TransactionId, ResourceTypeId) WHERE TransactionId IS NOT NULL 
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_HistoryTransactionId_ResourceTypeId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.Resource (HistoryTransactionId, ResourceTypeId) WHERE HistoryTransactionId IS NOT NULL 
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_ResourceIdInt_ResourceId_Version_ResourceTypeId ON dbo.Resource (ResourceIdInt, ResourceId, Version, ResourceTypeId)
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

-- Remove when history is separated
CREATE UNIQUE INDEX IXU_ResourceIdInt_ResourceId_ResourceTypeId_INCLUDE_Version_IsDeleted_WHERE_IsHistory_0 ON dbo.Resource (ResourceIdInt, ResourceId, ResourceTypeId)
  INCLUDE (Version, IsDeleted) WHERE IsHistory = 0
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

-- Remove when history is separated. Leaving old name for backward compatibility
CREATE UNIQUE INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource (ResourceTypeId, ResourceSurrogateId) WHERE IsHistory = 0 AND IsDeleted = 0
  WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
