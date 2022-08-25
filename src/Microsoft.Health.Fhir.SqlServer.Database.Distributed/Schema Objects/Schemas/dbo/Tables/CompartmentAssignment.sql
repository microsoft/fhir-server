CREATE TABLE dbo.CompartmentAssignment
(
    ResourceTypeId              smallint            NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    CompartmentTypeId           tinyint             NOT NULL,
    ReferenceResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                 NOT NULL,

    CONSTRAINT PKC_CompartmentAssignment PRIMARY KEY CLUSTERED (ResourceTypeId, TransactionId, ShardletId, Sequence, CompartmentTypeId, ReferenceResourceId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)
GO
--ALTER TABLE dbo.CompartmentAssignment SET ( LOCK_ESCALATION = AUTO )
GO
CREATE INDEX IX_ResourceTypeId_CompartmentTypeId_ReferenceResourceId_TransactionId_ShardletId_Sequence
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    TransactionId, ShardletId, Sequence
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO

