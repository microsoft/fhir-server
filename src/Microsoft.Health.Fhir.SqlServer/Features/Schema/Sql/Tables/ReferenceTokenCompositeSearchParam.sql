﻿CREATE TABLE dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
