CREATE TYPE dbo.BulkResourceWriteClaimTableType_1 AS TABLE
(
    Offset              int                 NOT NULL,
    ClaimTypeId         tinyint             NOT NULL,
    ClaimValue          nvarchar(128)       NOT NULL
)