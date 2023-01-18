CREATE TABLE dbo.NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NOT NULL,
    HighValue decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL
)
GO
ALTER TABLE dbo.NumberSearchParam ADD CONSTRAINT DF_NumberSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
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
GO
CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
