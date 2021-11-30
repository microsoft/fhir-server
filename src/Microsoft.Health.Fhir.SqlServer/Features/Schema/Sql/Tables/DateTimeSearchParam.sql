CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    CONSTRAINT PKC_DateTimeSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId)
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId),
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax bit CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
)

ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
