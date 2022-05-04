/*************************************************************
    Date Search Param
**************************************************************/

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetimeoffset(7) NOT NULL,
    EndDateTime datetimeoffset(7) NOT NULL,
    IsLongerThanADay bit NOT NULL
)