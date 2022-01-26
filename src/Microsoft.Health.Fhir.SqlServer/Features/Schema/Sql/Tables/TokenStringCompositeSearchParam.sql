CREATE TABLE dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NOT NULL CONSTRAINT DF_TokenStringCompositeSearchParam_SystemId1 DEFAULT 0,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_CI_AI NULL,
    IsHistory bit NOT NULL,
    TextHash2 binary(32) NOT NULL,
    CONSTRAINT PK_TokenStringCompositeSearchParam PRIMARY KEY NONCLUSTERED (ResourceTypeId, SearchParamId, ResourceSurrogateId, SystemId1, Code1, TextHash2)
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
)

ALTER TABLE dbo.TokenStringCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    TextOverflow2 -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND TextOverflow2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
