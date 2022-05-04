/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL
)