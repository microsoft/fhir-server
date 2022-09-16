CREATE TABLE CREATE TABLE QuantitySearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NOT NULL,
    HighValue decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL
)PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_QuantitySearchParam
ON QuantitySearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
ON QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    SingleValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 :: bit AND SingleValue IS NOT NULL;

CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_H
ON QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_L
ON QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 :: bit;
