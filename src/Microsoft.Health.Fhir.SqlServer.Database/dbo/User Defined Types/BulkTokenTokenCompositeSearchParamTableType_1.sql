CREATE TYPE [dbo].[BulkTokenTokenCompositeSearchParamTableType_1] AS TABLE (
    [Offset]        INT           NOT NULL,
    [SearchParamId] SMALLINT      NOT NULL,
    [SystemId1]     INT           NULL,
    [Code1]         VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    [SystemId2]     INT           NULL,
    [Code2]         VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

