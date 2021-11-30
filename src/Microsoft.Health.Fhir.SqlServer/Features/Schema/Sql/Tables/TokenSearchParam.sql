CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(128)            COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PK_TokenSearchParam PRIMARY KEY NONCLUSTERED (ResourceTypeId, SearchParamId, Code, ResourceSurrogateId)
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId),
    IsHistory                   bit                     NOT NULL,
)

ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
