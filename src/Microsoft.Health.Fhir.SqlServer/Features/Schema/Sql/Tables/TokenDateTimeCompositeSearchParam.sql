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
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)

WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)

WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
