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

ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT DF_TokenSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT CHK_TokenSearchParam_CodeOverflow CHECK (LEN(Code) = 256 OR CodeOverflow IS NULL)

ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_ResourceTypeId_SearchParamId_Code_ResourceSurrogateId_INCLUDE_SystemId_WHERE_IsHistory_0
ON dbo.TokenSearchParam (ResourceTypeId, SearchParamId, Code, ResourceSurrogateId) INCLUDE (SystemId) WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

