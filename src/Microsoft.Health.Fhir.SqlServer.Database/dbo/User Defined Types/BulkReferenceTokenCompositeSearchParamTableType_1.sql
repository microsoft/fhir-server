CREATE TYPE [dbo].[BulkReferenceTokenCompositeSearchParamTableType_1] AS TABLE (
    [Offset]                    INT           NOT NULL,
    [SearchParamId]             SMALLINT      NOT NULL,
    [BaseUri1]                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    [ReferenceResourceTypeId1]  SMALLINT      NULL,
    [ReferenceResourceId1]      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [ReferenceResourceVersion1] INT           NULL,
    [SystemId2]                 INT           NULL,
    [Code2]                     VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

