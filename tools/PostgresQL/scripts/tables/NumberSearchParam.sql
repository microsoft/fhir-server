CREATE TABLE NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NOT NULL,
    HighValue decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_NumberSearchParam
ON NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    SingleValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit AND SingleValue IS NOT NULL;

CREATE INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit;
