CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 nvarchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    LowValue3,
    HighValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
