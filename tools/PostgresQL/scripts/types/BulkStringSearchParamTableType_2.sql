CREATE TYPE BulkStringSearchParamTableType_2 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    Text varchar(256)  ,
    TextOverflow text  ,
    IsMin bit  ,
    IsMax bit  
)