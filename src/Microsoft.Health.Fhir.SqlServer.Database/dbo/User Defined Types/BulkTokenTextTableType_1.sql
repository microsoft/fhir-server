CREATE TYPE [dbo].[BulkTokenTextTableType_1] AS TABLE (
    [Offset]        INT            NOT NULL,
    [SearchParamId] SMALLINT       NOT NULL,
    [Text]          NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL);

