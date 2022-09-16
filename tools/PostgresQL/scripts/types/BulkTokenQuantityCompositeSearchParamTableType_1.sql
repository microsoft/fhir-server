CREATE TYPE BulkTokenQuantityCompositeSearchParamTableType_1 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    SystemId1 int  ,
    Code1 varchar(128)  ,
    SystemId2 int  ,
    QuantityCodeId2 int  ,
    SingleValue2 decimal(18,6)  ,
    LowValue2 decimal(18,6)  ,
    HighValue2 decimal(18,6)  
)