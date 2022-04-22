CREATE TYPE [dbo].[BulkStringSearchParamTableType_2] AS TABLE (
    [Offset]        INT            NOT NULL,
    [SearchParamId] SMALLINT       NOT NULL,
    [Text]          NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    [TextOverflow]  NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
    [IsMin]         BIT            NOT NULL,
    [IsMax]         BIT            NOT NULL);

