CREATE TABLE dbo.TokenText
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    SearchParamId               smallint            NOT NULL,
    Text                        nvarchar(400)       COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory                   bit                 NOT NULL
)
GO
ALTER TABLE dbo.TokenText ADD CONSTRAINT DF_TokenText_IsHistory DEFAULT 0 FOR IsHistory
GO
ALTER TABLE dbo.TokenText SET ( LOCK_ESCALATION = AUTO )
GO
CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
