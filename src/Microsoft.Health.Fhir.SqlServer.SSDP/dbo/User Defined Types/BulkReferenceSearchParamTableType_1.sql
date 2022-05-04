/*************************************************************
    Reference Search Param
**************************************************************/

CREATE TYPE dbo.BulkReferenceSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL
)