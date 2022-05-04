/*************************************************************
    Token Text
**************************************************************/

CREATE TYPE dbo.BulkTokenTextTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)