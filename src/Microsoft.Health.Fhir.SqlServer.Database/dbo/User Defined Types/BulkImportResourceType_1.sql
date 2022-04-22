CREATE TYPE [dbo].[BulkImportResourceType_1] AS TABLE (
    [ResourceTypeId]       SMALLINT        NOT NULL,
    [ResourceId]           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Version]              INT             NOT NULL,
    [IsHistory]            BIT             NOT NULL,
    [ResourceSurrogateId]  BIGINT          NOT NULL,
    [IsDeleted]            BIT             NOT NULL,
    [RequestMethod]        VARCHAR (10)    NULL,
    [RawResource]          VARBINARY (MAX) NOT NULL,
    [IsRawResourceMetaSet] BIT             DEFAULT ((0)) NOT NULL,
    [SearchParamHash]      VARCHAR (64)    NULL);

