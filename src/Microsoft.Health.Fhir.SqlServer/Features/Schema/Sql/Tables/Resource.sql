CREATE TABLE dbo.ResourceCurrent
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_ResourceCurrent_IsHistory DEFAULT 0, CONSTRAINT CH_ResourceCurrent_IsHistory CHECK (IsHistory = 0)
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NOT NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL      -- used for main CRUD operation 
   ,HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

    CONSTRAINT PKC_ResourceCurrent_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceCurrent_ResourceTypeId_ResourceId UNIQUE (ResourceTypeId, ResourceId) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT CH_ResourceCurrent_RawResource_Length CHECK (RawResource > 0x0)
)

ALTER TABLE dbo.ResourceCurrent SET ( LOCK_ESCALATION = AUTO )

CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceSurrgateId ON dbo.ResourceCurrent (ResourceTypeId, ResourceId) WHERE IsDeleted = 0 ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_ResourceTypeId_TransactionId ON dbo.ResourceCurrent (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId ON dbo.ResourceCurrent (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO

CREATE TABLE dbo.ResourceHistory
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_ResourceHistory_IsHistory DEFAULT 1, CONSTRAINT CH_ResourceHistory_IsHistory CHECK (IsHistory = 1)
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NOT NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL      -- used for main CRUD operation 
   ,HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

    CONSTRAINT PKC_ResourceHistory_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceHistory_ResourceTypeId_ResourceId_Version UNIQUE (ResourceTypeId, ResourceId, Version) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.ResourceHistory SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_ResourceTypeId_TransactionId ON dbo.ResourceHistory (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId ON dbo.ResourceHistory (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
