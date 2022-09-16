CREATE TYPE BulkQuantitySearchParamTableType_1 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    SystemId int  ,
    QuantityCodeId int  ,
    SingleValue decimal(18,6)  ,
    LowValue decimal(18,6)  ,
    HighValue decimal(18,6)  
)