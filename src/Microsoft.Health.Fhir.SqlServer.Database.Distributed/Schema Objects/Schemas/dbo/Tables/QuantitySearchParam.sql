CREATE TABLE dbo.QuantitySearchParam
(
    ResourceTypeId smallint NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NOT NULL,
    HighValue decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL
)
GO
--ALTER TABLE dbo.QuantitySearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    TransactionId, ShardletId, Sequence,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    SingleValue,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
