/*************************************************************
    Token Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)