/*************************************************************
    Quantity Search Param
**************************************************************/

-- See comment above for number search params for how we store ranges

CREATE TYPE dbo.BulkQuantitySearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)