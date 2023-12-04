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

ALTER TABLE dbo.TokenTokenCompositeSearchParam ADD CONSTRAINT DF_TokenTokenCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory

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
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2
ON dbo.TokenTokenCompositeSearchParam
(
    SearchParamId,
    Code1,
    Code2
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)
