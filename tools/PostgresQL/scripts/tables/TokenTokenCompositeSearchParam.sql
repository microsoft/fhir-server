CREATE TABLE TokenTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) NOT NULL,
    IsHistory bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_TokenTokenCompositeSearchParam
ON TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
ON TokenTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 :: bit;
