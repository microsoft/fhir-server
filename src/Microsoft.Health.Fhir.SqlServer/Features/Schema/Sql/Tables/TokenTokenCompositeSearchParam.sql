CREATE TABLE dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    CodeOverflow2 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
)
GO
--ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
