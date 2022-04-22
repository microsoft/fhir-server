CREATE TYPE [dbo].[BulkTokenNumberNumberCompositeSearchParamTableType_1] AS TABLE (
    [Offset]        INT             NOT NULL,
    [SearchParamId] SMALLINT        NOT NULL,
    [SystemId1]     INT             NULL,
    [Code1]         VARCHAR (128)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    [SingleValue2]  DECIMAL (18, 6) NULL,
    [LowValue2]     DECIMAL (18, 6) NULL,
    [HighValue2]    DECIMAL (18, 6) NULL,
    [SingleValue3]  DECIMAL (18, 6) NULL,
    [LowValue3]     DECIMAL (18, 6) NULL,
    [HighValue3]    DECIMAL (18, 6) NULL,
    [HasRange]      BIT             NOT NULL);

