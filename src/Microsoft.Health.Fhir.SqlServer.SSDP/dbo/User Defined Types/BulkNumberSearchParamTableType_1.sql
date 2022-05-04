/*************************************************************
    Number Search Param
**************************************************************/

-- We support the underlying value being a range, though we expect the vast majority of entries to be a single value.
-- Either:
--  (1) SingleValue is not null and LowValue and HighValue are both null, or
--  (2) SingleValue is null and LowValue and HighValue are both not null
-- We make use of filtered nonclustered indexes to keep queries over the ranges limited to those rows that actually have ranges

CREATE TYPE dbo.BulkNumberSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)