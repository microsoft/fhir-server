/*************************************************************
    Token$DateTime Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2 datetimeoffset(7) NOT NULL,
    EndDateTime2 datetimeoffset(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL
)