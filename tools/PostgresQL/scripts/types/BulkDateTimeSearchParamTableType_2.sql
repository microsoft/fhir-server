CREATE TYPE BulkDateTimeSearchParamTableType_2 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    StartDateTime time with time zone  ,
    EndDateTime time with time zone  ,
    IsLongerThanADay bit  ,
    IsMin bit  ,
    IsMax bit  
)
