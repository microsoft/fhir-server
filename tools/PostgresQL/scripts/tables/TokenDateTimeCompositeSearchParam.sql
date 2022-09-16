CREATE TABLE TokenDateTimeCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    StartDateTime2 time with time zone NOT NULL,
    EndDateTime2 time with time zone NOT NULL,
    IsLongerThanADay2 bit NOT NULL,
    IsHistory bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_TokenDateTimeCompositeSearchParam
ON TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_TokenDateTimeCompositeSearchParam_EndDateTime2
ON TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)

WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_TokenDateTimeCompositeSearchParam_StartDateTime2
ON TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_TokenDateTimeCompositeSearchParam_EndDateTime2_Long
ON TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)

WHERE IsHistory = 0 :: bit AND IsLongerThanADay2 = 1 :: bit;

CREATE INDEX IX_TokenDateTimeCompositeSearchParam_StartDateTime2_Long
ON TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 :: bit AND IsLongerThanADay2 = 1 :: bit;
