CREATE TABLE dbo.ResourceCurrent
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL  CHECK (IsHistory = 0),
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NOT NULL,
    IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0,
    SearchParamHash             varchar(64)             NULL
)

ALTER TABLE dbo.ResourceCurrent SET ( LOCK_ESCALATION = AUTO )

--TODO: Fix name
CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.ResourceCurrent
(
    ResourceTypeId,
    ResourceSurrogateId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

--TODO: Fix name
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.ResourceCurrent
(
    ResourceTypeId,
    ResourceId,
    Version
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

--TODO: Fix name
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.ResourceCurrent
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

--TODO: Fix name
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.ResourceCurrent
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsDeleted = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

--TODO: Fix name
CREATE NONCLUSTERED INDEX IX_Resource_ResourceSurrogateId ON dbo.ResourceCurrent
(
    ResourceSurrogateId
)
ON [Primary]

GO

CREATE TABLE dbo.ResourceHistory
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version                     int                     NOT NULL
   ,IsHistory                   bit                     NOT NULL   CHECK (IsHistory = 1)
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,IsDeleted                   bit                     NOT NULL
   ,RequestMethod               varchar(10)             NULL
   ,RawResource                 varbinary(max)          NOT NULL
   ,IsRawResourceMetaSet        bit                     NOT NULL
   ,SearchParamHash             varchar(64)             NULL
   ,HistoryDate                 datetime                NOT NULL   CONSTRAINT DF_ResourceHistory_HistoryDate DEFAULT getUTCdate()
)

ALTER TABLE dbo.ResourceHistory ADD CONSTRAINT PKC_ResourceHistory_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

-- This allows query on history for given resource (resource id)
CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceId_Version ON dbo.ResourceHistory (ResourceTypeId, ResourceId, Version)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

-- TODO: Do we need this index?
CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceSurrogateId ON dbo.ResourceHistory (ResourceTypeId, ResourceSurrogateId)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

-- This index will be needed when archiving requirements are implemented
CREATE INDEX IX_HistoryDate_ResourceTypeId ON dbo.ResourceHistory (HistoryDate, ResourceTypeId)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
CREATE VIEW dbo.Resource
AS
SELECT ResourceTypeId
      ,ResourceId
      ,Version
      ,IsHistory
      ,ResourceSurrogateId
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash 
  FROM dbo.ResourceCurrent
UNION ALL
SELECT ResourceTypeId
      ,ResourceId
      ,Version
      ,IsHistory
      ,ResourceSurrogateId
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash 
  FROM dbo.ResourceHistory
GO
