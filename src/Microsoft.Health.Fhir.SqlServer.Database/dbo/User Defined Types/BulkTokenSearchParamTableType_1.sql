CREATE TYPE [dbo].[BulkTokenSearchParamTableType_1] AS TABLE (
    [Offset]        INT           NOT NULL,
    [SearchParamId] SMALLINT      NOT NULL,
    [SystemId]      INT           NULL,
    [Code]          VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

