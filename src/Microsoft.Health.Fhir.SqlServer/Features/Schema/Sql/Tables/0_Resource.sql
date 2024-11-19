CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL
   ,HistoryTransactionId        bigint                  NULL
   ,FileId                      bigint                  NULL
   ,OffsetInFile                int                     NULL
)
GO
DROP TABLE dbo.Resource
GO
CREATE TABLE dbo.ResourceIdIntMap
(
    ResourceTypeId  smallint    NOT NULL
   ,ResourceIdInt   bigint      NOT NULL
   ,ResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
    
    CONSTRAINT PKC_ResourceIdIntMap_ResourceIdInt_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceIdIntMap_ResourceId_ResourceTypeId UNIQUE (ResourceId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.ResourceIdIntMap SET ( LOCK_ESCALATION = AUTO )
GO
CREATE TABLE dbo.RawResources
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,RawResource                 varbinary(max)          NULL

    CONSTRAINT PKC_RawResources_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.RawResources SET ( LOCK_ESCALATION = AUTO )
GO
CREATE TABLE dbo.CurrentResources
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceIdInt               bigint                  NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsHistory DEFAULT 0, CONSTRAINT CH_ResourceCurrent_IsHistory CHECK (IsHistory = 0)
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsRawResourceMetaSet DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL
   ,HistoryTransactionId        bigint                  NULL
   ,FileId                      bigint                  NULL
   ,OffsetInFile                int                     NULL

    CONSTRAINT PKC_CurrentResources_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_CurrentResources_ResourceIdInt_ResourceTypeId UNIQUE (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.CurrentResources ADD CONSTRAINT FK_CurrentResources_ResourceIdInt_ResourceTypeId_ResourceIdIntMap FOREIGN KEY (ResourceIdInt, ResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

ALTER TABLE dbo.CurrentResources SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_TransactionId_ResourceTypeId_WHERE_TransactionId_NOT_NULL ON dbo.CurrentResources (TransactionId, ResourceTypeId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_HistoryTransactionId_ResourceTypeId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.CurrentResources (HistoryTransactionId, ResourceTypeId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
CREATE TABLE dbo.HistoryResources
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceIdInt               bigint                  NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_HistoryResources_IsHistory DEFAULT 1, CONSTRAINT CH_HistoryResources_IsHistory CHECK (IsHistory = 1)
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_HistoryResources_IsRawResourceMetaSet DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL
   ,HistoryTransactionId        bigint                  NULL
   ,FileId                      bigint                  NULL
   ,OffsetInFile                int                     NULL

    CONSTRAINT PKC_HistoryResources_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_HistoryResources_ResourceIdInt_Version_ResourceTypeId UNIQUE (ResourceIdInt, Version, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.HistoryResources ADD CONSTRAINT FK_HistoryResources_ResourceIdInt_ResourceTypeId_ResourceIdIntMap FOREIGN KEY (ResourceIdInt, ResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

ALTER TABLE dbo.HistoryResources SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_TransactionId_ResourceTypeId_WHERE_TransactionId_NOT_NULL ON dbo.HistoryResources (TransactionId, ResourceTypeId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_HistoryTransactionId_ResourceTypeId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.HistoryResources (HistoryTransactionId, ResourceTypeId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
