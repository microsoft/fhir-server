CREATE TABLE UriSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) NOT NULL,
    IsHistory bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_UriSearchParam
ON UriSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_UriSearchParam_SearchParamId_Uri
ON UriSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Uri,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit;
