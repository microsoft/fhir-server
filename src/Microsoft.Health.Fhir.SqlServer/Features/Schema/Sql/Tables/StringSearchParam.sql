CREATE TABLE dbo.StringSearchParam
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin bit CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax bit CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
)

ALTER TABLE dbo.StringSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE COLUMNSTORE INDEX IX_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
)
WHERE TextOverflow IS NOT NULL
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

