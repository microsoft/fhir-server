CREATE TYPE [dbo].[BulkNumberSearchParamTableType_1] AS TABLE (
    [Offset]        INT             NOT NULL,
    [SearchParamId] SMALLINT        NOT NULL,
    [SingleValue]   DECIMAL (18, 6) NULL,
    [LowValue]      DECIMAL (18, 6) NULL,
    [HighValue]     DECIMAL (18, 6) NULL);

