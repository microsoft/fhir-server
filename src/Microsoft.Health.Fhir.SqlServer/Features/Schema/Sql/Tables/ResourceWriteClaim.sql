CREATE TABLE dbo.ResourceWriteClaim
(
    ResourceSurrogateId             bigint              NOT NULL,
    ClaimTypeId                     tinyint             NOT NULL,
    ClaimValue                      nvarchar(128)       NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ResourceWriteClaim on dbo.ResourceWriteClaim
(
    ResourceSurrogateId,
    ClaimTypeId
)
