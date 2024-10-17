CREATE TABLE dbo.RawResources
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,RawResource                 varbinary(max)          NULL

    CONSTRAINT PKC_RawResources_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.RawResources SET ( LOCK_ESCALATION = AUTO )
GO
CREATE TABLE dbo.ResourceCurrent
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_ResourceCurrent_IsHistory DEFAULT 0, CONSTRAINT CH_ResourceCurrent_IsHistory CHECK (IsHistory = 0)
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_ResourceCurrent_IsRawResourceMetaSet DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL      -- used for main CRUD operation 
   ,HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

    CONSTRAINT PKC_ResourceCurrent_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceCurrent_ResourceTypeId_ResourceId UNIQUE (ResourceTypeId, ResourceId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.ResourceCurrent SET ( LOCK_ESCALATION = AUTO )

-- Strictly speaking, IsHistory=0 filter in the index is not required because table has check constraint. 
-- But SQL Server has a bug. For queries having IsHistory=0 in WHERE, SQL knows that only current table should be looked at.
-- But when choosing indexes it forgets about this unless IsHistory=0 is also included in index definition.
-- Without this redundant filtering clause in the index SQL chooses clustered index scan. For large tables it is a difference between running OK and timing out. 
--CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceSurrogateId_WHERE_IsHistory_0_IsDeleted_0 ON dbo.ResourceCurrent (ResourceTypeId, ResourceSurrogateId) WHERE IsHistory = 0 AND IsDeleted = 0 WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_TransactionId_WHERE_TransactionId_NOT_NULL ON dbo.ResourceCurrent (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.ResourceCurrent (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
EXECUTE sp_rename 'ResourceCurrent', 'ResourceCurrentTbl'
GO
CREATE VIEW dbo.ResourceCurrent
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
  FROM dbo.ResourceCurrentTbl A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
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
   ,RawResource                 varbinary(max)          NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_ResourceHistory_IsRawResourceMetaSet DEFAULT 0
   ,SearchParamHash             varchar(64)             NULL
   ,TransactionId               bigint                  NULL      -- used for main CRUD operation 
   ,HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

    CONSTRAINT PKC_ResourceHistory_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceHistory_ResourceTypeId_ResourceId_Version UNIQUE (ResourceTypeId, ResourceId, Version) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.ResourceHistory SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_ResourceTypeId_TransactionId_WHERE_TransactionId_NOT_NULL ON dbo.ResourceHistory (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.ResourceHistory (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
EXECUTE sp_rename 'ResourceHistory', 'ResourceHistoryTbl'
GO
CREATE VIEW dbo.ResourceHistory
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId
      ,HistoryTransactionId
  FROM dbo.ResourceHistoryTbl A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
GO
CREATE TABLE dbo.Dummy (Dummy int)
GO
