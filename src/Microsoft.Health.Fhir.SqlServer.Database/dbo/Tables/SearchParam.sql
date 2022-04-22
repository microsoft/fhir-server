CREATE TABLE [dbo].[SearchParam] (
    [SearchParamId]        SMALLINT           IDENTITY (1, 1) NOT NULL,
    [Uri]                  VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Status]               VARCHAR (10)       NULL,
    [LastUpdated]          DATETIMEOFFSET (7) NULL,
    [IsPartiallySupported] BIT                NULL,
    CONSTRAINT [PKC_SearchParam] PRIMARY KEY CLUSTERED ([Uri] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [UQ_SearchParam_SearchParamId] UNIQUE NONCLUSTERED ([SearchParamId] ASC)
);

