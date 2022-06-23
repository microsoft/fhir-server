CREATE TABLE dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId              smallint NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId               smallint NOT NULL,
    SystemId1                   int NULL,
    Code1                       varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2                   int NULL,
    Code2                       varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit NOT NULL
)
GO
--ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_TransactionId_ShardletId_Sequence_SearchParamId
ON dbo.TokenTokenCompositeSearchParam
(
    TransactionId,
    ShardletId,
    Sequence,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2 --TODO: Change name
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Code2,
    TransactionId,
    ShardletId,
    Sequence
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
