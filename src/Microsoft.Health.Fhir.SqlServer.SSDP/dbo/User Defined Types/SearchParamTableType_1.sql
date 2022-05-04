/*************************************************************
    Search Parameter Status Information
**************************************************************/

-- We adopted this naming convention for table-valued parameters because they are immutable.
CREATE TYPE dbo.SearchParamTableType_1 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    IsPartiallySupported bit NOT NULL
)