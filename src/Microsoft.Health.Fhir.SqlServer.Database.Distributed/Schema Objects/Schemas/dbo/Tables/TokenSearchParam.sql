CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId              smallint                NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(128)            COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                     NOT NULL,
)
GO
--ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    TransactionId, ShardletId, Sequence,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    TransactionId, ShardletId, Sequence
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
