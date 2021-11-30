CREATE TABLE dbo.NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) SPARSE NULL,
    HighValue decimal(18,6) SPARSE NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    SingleValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
