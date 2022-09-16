CREATE TABLE StringSearchParam
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    SearchParamId smallint NOT NULL,
    Text varchar(256) NOT NULL,
    TextOverflow text NULL,
    IsHistory bit NOT NULL,
    IsMin bit CONSTRAINT string_IsMin_Constraint DEFAULT 0 :: bit NOT NULL,
    IsMax bit CONSTRAINT string_IsMax_Constraint DEFAULT 0 :: bit NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_StringSearchParam
ON StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_StringSearchParam_SearchParamId_Text
ON StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    TextOverflow, -- will not be needed when all servers are targeting at least this version.
    IsMin,
    IsMax
)
WHERE IsHistory = 0 :: bit;

CREATE INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 :: bit AND TextOverflow IS NOT NULL;
