CREATE TYPE [dbo].[BulkReferenceSearchParamTableType_1] AS TABLE (
    [Offset]                   INT           NOT NULL,
    [SearchParamId]            SMALLINT      NOT NULL,
    [BaseUri]                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    [ReferenceResourceTypeId]  SMALLINT      NULL,
    [ReferenceResourceId]      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [ReferenceResourceVersion] INT           NULL);

