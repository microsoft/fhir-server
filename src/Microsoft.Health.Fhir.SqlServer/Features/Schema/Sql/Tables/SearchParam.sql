CREATE TABLE dbo.SearchParam
(
    SearchParamId           smallint IDENTITY(1,1)      NOT NULL,
    CONSTRAINT UQ_SearchParam_SearchParamId UNIQUE (SearchParamId),
    Uri                     varchar(128)                COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri)
    WITH (DATA_COMPRESSION = PAGE),
    Status                  varchar(10)                 NULL,
    LastUpdated             datetimeoffset(7)           NULL,
    IsPartiallySupported    bit                         NULL
)
