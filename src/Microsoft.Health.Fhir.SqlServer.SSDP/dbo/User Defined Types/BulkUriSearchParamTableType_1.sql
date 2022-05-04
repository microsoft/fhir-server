/*************************************************************
    URI Search Param
**************************************************************/

CREATE TYPE dbo.BulkUriSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
)