CREATE TABLE dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2 datetime2(7) NOT NULL,
    EndDateTime2 datetime2(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL,
    IsHistory bit NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
)

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam ADD CONSTRAINT DF_TokenDateTimeCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam ADD CONSTRAINT CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256 OR CodeOverflow1 IS NULL)

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2
)
INCLUDE
(
    SystemId1
)
WHERE IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2
)
INCLUDE
(
    SystemId1
)
WHERE IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

