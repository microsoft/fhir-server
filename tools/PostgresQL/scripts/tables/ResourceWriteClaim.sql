CREATE TABLE ResourceWriteClaim
(
    ResourceSurrogateId             bigint              NOT NULL,
    ClaimTypeId                     int            NOT NULL,
    ClaimValue                      varchar(128)       NOT NULL
);

CREATE INDEX IXC_ResourceWriteClaim on ResourceWriteClaim
(
    ResourceSurrogateId,
    ClaimTypeId
);
