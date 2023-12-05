CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax bit CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
)

ALTER TABLE dbo.DateTimeSearchParam ADD CONSTRAINT DF_DateTimeSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    StartDateTime,
    EndDateTime -- TODO: Should it be in INCLUDE?
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    EndDateTime,
    StartDateTime -- TODO: Should it be in INCLUDE?
)
INCLUDE
(
    IsLongerThanADay,
    IsMin,
    IsMax
)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    StartDateTime,
    EndDateTime -- TODO: Should it be in INCLUDE?
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    EndDateTime,
    StartDateTime -- TODO: Should it be in INCLUDE?
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

