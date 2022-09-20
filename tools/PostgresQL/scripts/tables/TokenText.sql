CREATE TABLE TokenText
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    ResourceId                  varchar(64)             NOT NULL,
    Version                     int                     NOT NULL,
    SearchParamId               int            NOT NULL,
    Text                        varchar(400)       NOT NULL,
    IsHistory                   bit                 NOT NULL
);

CREATE  INDEX IXC_TokenText
ON TokenText
(
    ResourceTypeId,
    ResourceId,
    Version,
    SearchParamId
);

CREATE INDEX IX_TokenText_SearchParamId_Text
ON TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceId,
    Version
)
WHERE IsHistory = 0 :: bit;

SELECT create_distributed_table('tokentext', 'resourceid', colocate_with => 'resource');
