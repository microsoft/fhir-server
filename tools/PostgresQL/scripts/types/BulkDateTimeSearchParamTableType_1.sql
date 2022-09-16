CREATE TYPE BulkDateTimeSearchParamTableType_1 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    StartDateTime time with time zone  ,
    EndDateTime time with time zone  ,
    IsLongerThanADay bit  
)
