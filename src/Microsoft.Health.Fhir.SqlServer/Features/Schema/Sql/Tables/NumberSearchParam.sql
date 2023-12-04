CREATE TABLE dbo.NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(36,18) NULL,
    LowValue decimal(36,18) NOT NULL,
    HighValue decimal(36,18) NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.NumberSearchParam ADD CONSTRAINT DF_NumberSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL
ON dbo.NumberSearchParam
(
    SearchParamId,
    SingleValue
)
WHERE SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    LowValue,
    HighValue
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    HighValue,
    LowValue
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

