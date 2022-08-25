CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax bit CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
)
GO
--ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    TransactionId, ShardletId, Sequence,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
