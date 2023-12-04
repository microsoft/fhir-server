CREATE TABLE dbo.UriSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.UriSearchParam ADD CONSTRAINT DF_UriSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.UriSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Uri
ON dbo.UriSearchParam
(
    SearchParamId,
    Uri
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

