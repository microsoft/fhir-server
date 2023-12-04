CREATE TABLE dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
    CodeOverflow2 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
)

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam ADD CONSTRAINT DF_ReferenceTokenCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam ADD CONSTRAINT CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2 CHECK (LEN(Code2) = 256 OR CodeOverflow2 IS NULL)

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2
ON dbo.ReferenceTokenCompositeSearchParam
(
    SearchParamId,
    ReferenceResourceId1,
    Code2
)
INCLUDE
(
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

