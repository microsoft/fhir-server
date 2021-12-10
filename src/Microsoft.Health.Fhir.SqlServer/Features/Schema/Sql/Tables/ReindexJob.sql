/*************************************************************
    Reindex Job
**************************************************************/
CREATE TABLE dbo.ReindexJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED (Id),
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)
