CREATE TABLE ReferenceSearchParam
(
    ResourceTypeId                      smallint                NOT NULL,
    ResourceSurrogateId                 bigint                  NOT NULL,
    SearchParamId                       smallint                NOT NULL,
    BaseUri                             varchar(128)            NULL,
    ReferenceResourceTypeId             smallint                NULL,
    ReferenceResourceId                 varchar(64)             NOT NULL,
    ReferenceResourceVersion            int                     NULL,
    IsHistory                           bit                     NOT NULL
) PARTITION BY RANGE(ResourceTypeId);

CREATE INDEX IXC_ReferenceSearchParam
ON ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
);

CREATE INDEX IX_ReferenceSearchParam_SearchParamId_XX
ON ReferenceSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceVersion
)
WHERE IsHistory = 0 :: bit;
