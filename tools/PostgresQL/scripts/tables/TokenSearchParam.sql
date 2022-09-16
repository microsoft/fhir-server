CREATE TABLE TokenSearchParam
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(128)            NOT NULL,
    IsHistory                   bit                     NOT NULL
)PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_TokenSearchParam
ON TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 :: bit;
