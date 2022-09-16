CREATE TABLE DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime time with time zone NOT NULL,
    EndDateTime time with time zone NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT date_IsMin_Constraint DEFAULT 0 :: bit NOT NULL,
    IsMax bit CONSTRAINT date_IsMax_Constraint DEFAULT 0 :: bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_DateTimeSearchParam
ON DateTimeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON DateTimeSearchParam
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
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON DateTimeSearchParam
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
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime1
ON DateTimeSearchParam
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
WHERE IsHistory = 0 :: bit AND IsLongerThanADay = 1 :: bit;

CREATE INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime1
ON DateTimeSearchParam
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
WHERE IsHistory = 0 :: bit AND IsLongerThanADay = 1 :: bit;
