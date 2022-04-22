CREATE TYPE [dbo].[SearchParamTableType_1] AS TABLE (
    [Uri]                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Status]               VARCHAR (10)  NOT NULL,
    [IsPartiallySupported] BIT           NOT NULL);

