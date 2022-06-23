CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId (smallint)
AS RANGE RIGHT FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);
GO
CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId
AS PARTITION PartitionFunction_ResourceTypeId ALL TO ([PRIMARY]);
GO
CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    SearchParamHash             varchar(64)             NULL,

    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, TransactionId, ShardletId, Sequence) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId),
)
GO
--ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )
GO
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    TransactionId, ShardletId, Sequence
)
WHERE IsHistory = 0 AND IsDeleted = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
