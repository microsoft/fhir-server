﻿CREATE TABLE dbo.StringSearchParam
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax bit CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
)

ALTER TABLE dbo.StringSearchParam ADD CONSTRAINT DF_StringSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.StringSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    TextOverflow, -- will not be needed when all servers are targeting at least this version.
    IsMin,
    IsMax
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

