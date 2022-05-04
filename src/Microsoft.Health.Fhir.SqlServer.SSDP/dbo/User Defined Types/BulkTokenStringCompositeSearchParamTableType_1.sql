/*************************************************************
    Token$String Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)