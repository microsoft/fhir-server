CREATE TYPE [dbo].[BulkUriSearchParamTableType_1] AS TABLE (
    [Offset]        INT           NOT NULL,
    [SearchParamId] SMALLINT      NOT NULL,
    [Uri]           VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL);

