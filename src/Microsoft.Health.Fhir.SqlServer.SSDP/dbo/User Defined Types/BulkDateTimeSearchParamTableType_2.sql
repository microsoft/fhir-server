/*************************************************************
    Date Search Param
**************************************************************/

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetimeoffset(7) NOT NULL,
    EndDateTime datetimeoffset(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsMin bit NOT NULL,
    IsMax bit NOT NULL
)