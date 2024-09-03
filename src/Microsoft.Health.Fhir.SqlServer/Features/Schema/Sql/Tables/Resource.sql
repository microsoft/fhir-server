CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceIdInt               bigint                  NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NULL,
    IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0,
    SearchParamHash             varchar(64)             NULL,
    TransactionId               bigint                  NULL,     -- used for main CRUD operation 
    HistoryTransactionId        bigint                  NULL,     -- used by CRUD operation that moved resource version in invisible state 
    OffsetInFile                int                     NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NULL -- just to build

    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId)
   ,CONSTRAINT CH_Resource_RawResource_OffsetInFile CHECK (RawResource IS NOT NULL OR OffsetInFile IS NOT NULL)
)

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

CREATE INDEX IX_ResourceTypeId_TransactionId ON dbo.Resource (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId ON dbo.Resource (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceIdInt_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceIdInt,
    Version
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE UNIQUE INDEX IXU_Resource_ResourceTypeId_ResourceIdInt ON dbo.Resource
(
    ResourceTypeId,
    ResourceIdInt
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)
