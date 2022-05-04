/*************************************************************
    Token$Number$Number Composite Search Param
**************************************************************/

-- See number search param for how we deal with null. We apply a similar pattern here,
-- except that we pass in a HasRange bit though the TVP. The alternative would have
-- for a computed column, but a computed column cannot be used in as a index filter
-- (even if it is a persisted computed column).

CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL
)