CREATE TABLE dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NOT NULL CONSTRAINT DF_TokenQuantityCompositeSearchParam_SystemId1 DEFAULT 0,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NOT NULL CONSTRAINT DF_TokenQuantityCompositeSearchParam_SystemId2 DEFAULT 0,
    QuantityCodeId2 int NOT NULL CONSTRAINT DF_TokenQuantityCompositeSearchParam_QuantityCodeId2 DEFAULT 0,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NOT NULL,
    HighValue2 decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL,
    CONSTRAINT PK_TokenQuantityCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
    WITH (DATA_COMPRESSION = PAGE) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
)

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND SingleValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    HighValue2,
    LowValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
