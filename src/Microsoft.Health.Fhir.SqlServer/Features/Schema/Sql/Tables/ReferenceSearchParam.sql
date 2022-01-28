
CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId                      smallint                NOT NULL,
    ResourceSurrogateId                 bigint                  NOT NULL,
    SearchParamId                       smallint                NOT NULL,
    BaseUri                             varchar(128)            COLLATE Latin1_General_100_CS_AS NOT NULL CONSTRAINT DF_ReferenceSearchParam_BaseUri DEFAULT '',
    ReferenceResourceTypeId             smallint                NOT NULL CONSTRAINT DF_ReferenceSearchParam_ReferenceResourceTypeId DEFAULT 0,
    ReferenceResourceId                 varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion            int                     NULL,
    IsHistory                           bit                     NOT NULL,
    CONSTRAINT PK_ReferenceSearchParam PRIMARY KEY NONCLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId )
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
)

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceVersion
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
