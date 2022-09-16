CREATE TABLE TokenStringCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    Text2 varchar(256) NOT NULL,
    TextOverflow2 text NULL,
    IsHistory bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE  INDEX IXC_TokenStringCompositeSearchParam
ON TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_TokenStringCompositeSearchParam_Code1_Text2
ON TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    TextOverflow2 -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_TokenStringCompositeSearchParam_Text2WithOverflow
ON TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 :: bit AND TextOverflow2 IS NOT NULL;
