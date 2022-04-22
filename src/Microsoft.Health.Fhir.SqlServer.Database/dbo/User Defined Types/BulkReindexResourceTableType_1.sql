CREATE TYPE [dbo].[BulkReindexResourceTableType_1] AS TABLE (
    [Offset]          INT          NOT NULL,
    [ResourceTypeId]  SMALLINT     NOT NULL,
    [ResourceId]      VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    [ETag]            INT          NULL,
    [SearchParamHash] VARCHAR (64) NOT NULL);

