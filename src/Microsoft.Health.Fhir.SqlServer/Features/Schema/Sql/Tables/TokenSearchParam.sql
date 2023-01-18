CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(256)            COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                     NOT NULL,
    CodeOverflow                varchar(max)           COLLATE Latin1_General_100_CS_AS NULL,
)
GO
ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT DF_TokenSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT CHK_TokenSearchParam_CodeOverflow CHECK (LEN(Code) = 256 OR CodeOverflow IS NULL)
GO
ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
