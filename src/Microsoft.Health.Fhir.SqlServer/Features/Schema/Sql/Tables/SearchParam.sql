CREATE TABLE dbo.SearchParam
(
    SearchParamId           smallint IDENTITY(1,1)      NOT NULL,
    CONSTRAINT UQ_SearchParam_SearchParamId UNIQUE (SearchParamId),
    Uri                     varchar(128)                COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri)
    WITH (DATA_COMPRESSION = PAGE),
    Status                  varchar(20)                 NOT NULL,
    LastUpdated             datetimeoffset(7)           NOT NULL,
    IsPartiallySupported    bit                         NOT NULL
)

CREATE NONCLUSTERED INDEX IX_LastUpdated
ON dbo.SearchParam (LastUpdated)
