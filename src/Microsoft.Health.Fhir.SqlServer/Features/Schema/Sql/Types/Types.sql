CREATE TYPE dbo.SearchParamTableType_2 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(20) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag int NULL,
    SearchParamHash varchar(64) NOT NULL
)
