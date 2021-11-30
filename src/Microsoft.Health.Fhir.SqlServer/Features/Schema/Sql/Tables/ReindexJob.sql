/*************************************************************
    Reindex Job
**************************************************************/
CREATE TABLE dbo.ReindexJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ReindexJob ON dbo.ReindexJob
(
    Id
)
