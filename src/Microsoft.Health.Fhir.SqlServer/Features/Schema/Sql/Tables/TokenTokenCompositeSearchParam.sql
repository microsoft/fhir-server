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
    CodeOverflow1 nvarchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    CodeOverflow2 nvarchar(max) COLLATE Latin1_General_100_CS_AS NULL,
)

ALTER TABLE dbo.TokenTokenCompositeSearchParam ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256 OR CodeOverflow1 IS NULL)
ALTER TABLE dbo.TokenTokenCompositeSearchParam ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow2 CHECK (LEN(Code2) = 256 OR CodeOverflow2 IS NULL)

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

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
