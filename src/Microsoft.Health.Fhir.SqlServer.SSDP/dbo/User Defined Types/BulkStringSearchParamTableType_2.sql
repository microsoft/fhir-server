/*************************************************************
    String Search Param
**************************************************************/

CREATE TYPE dbo.BulkStringSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin bit NOT NULL,
    IsMax bit NOT NULL
)