CREATE TYPE [dbo].[BulkTokenStringCompositeSearchParamTableType_1] AS TABLE (
    [Offset]        INT            NOT NULL,
    [SearchParamId] SMALLINT       NOT NULL,
    [SystemId1]     INT            NULL,
    [Code1]         VARCHAR (128)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Text2]         NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    [TextOverflow2] NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);

