CREATE TABLE TokenText
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    SearchParamId               int            NOT NULL,
    Text                        varchar(400)       NOT NULL,
    IsHistory                   bit                 NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE  INDEX IXC_TokenText
ON TokenText
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_TokenText_SearchParamId_Text
ON TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit;
