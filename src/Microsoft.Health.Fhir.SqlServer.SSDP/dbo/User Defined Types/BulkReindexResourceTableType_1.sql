CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag int NULL,
    SearchParamHash varchar(64) NOT NULL
)