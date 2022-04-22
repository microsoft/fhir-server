CREATE TYPE [dbo].[BulkQuantitySearchParamTableType_1] AS TABLE (
    [Offset]         INT             NOT NULL,
    [SearchParamId]  SMALLINT        NOT NULL,
    [SystemId]       INT             NULL,
    [QuantityCodeId] INT             NULL,
    [SingleValue]    DECIMAL (18, 6) NULL,
    [LowValue]       DECIMAL (18, 6) NULL,
    [HighValue]      DECIMAL (18, 6) NULL);

