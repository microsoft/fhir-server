CREATE TYPE BulkReferenceSearchParamTableType_1 AS
(
    "Offset" int  ,
    SearchParamId smallint  ,
    BaseUri varchar(128)  ,
    ReferenceResourceTypeId smallint  ,
    ReferenceResourceId varchar(64)  ,
    ReferenceResourceVersion int  
)