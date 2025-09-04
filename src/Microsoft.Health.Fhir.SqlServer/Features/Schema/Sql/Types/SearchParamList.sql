--DROP TYPE dbo.SearchParamList
GO
CREATE TYPE dbo.SearchParamList AS TABLE
(
    Uri                      varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Status                   varchar(20) NOT NULL
   ,IsPartiallySupported     bit NOT NULL
   ,LastUpdated              datetimeoffset(7) NOT NULL

   UNIQUE (Uri)
)
GO
