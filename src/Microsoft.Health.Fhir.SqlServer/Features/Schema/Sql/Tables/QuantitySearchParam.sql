CREATE TABLE dbo.QuantitySearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(36,18) NULL,
    LowValue decimal(36,18) NOT NULL,
    HighValue decimal(36,18) NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.QuantitySearchParam ADD CONSTRAINT DF_QuantitySearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.QuantitySearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    SingleValue
)
INCLUDE
(
    SystemId
)
WHERE SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue
)
INCLUDE
(
    SystemId
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue
)
INCLUDE
(
    SystemId
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

