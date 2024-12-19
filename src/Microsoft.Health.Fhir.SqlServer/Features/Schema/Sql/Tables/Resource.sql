-- Our code generator, that creates class wrappers on database objects, is not able to deal with views, but we stil want to refer to view objects in the code.
-- Workaround is to create table that looks like view, so code generator picks it up, and immediately drop it.
-- This would not be needed at all, if we followed different class generation strategy.
CREATE TABLE dbo.CurrentResource -- This is replaced by view CurrentResource
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NOT NULL,
    IsRawResourceMetaSet        bit                     NOT NULL,
    SearchParamHash             varchar(64)             NULL,
    TransactionId               bigint                  NULL,
    HistoryTransactionId        bigint                  NULL
)
GO
DROP TABLE dbo.CurrentResource
GO
CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NOT NULL,
    IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0,
    SearchParamHash             varchar(64)             NULL,
    TransactionId               bigint                  NULL,     -- used for main CRUD operation 
    HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId),
    CONSTRAINT CH_Resource_RawResource_Length CHECK (RawResource > 0x0)
)

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_ResourceTypeId_TransactionId ON dbo.Resource (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId ON dbo.Resource (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
