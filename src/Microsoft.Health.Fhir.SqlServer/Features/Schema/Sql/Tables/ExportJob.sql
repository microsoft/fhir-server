/*************************************************************
    Export Job
**************************************************************/
CREATE TABLE dbo.ExportJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_ExportJob PRIMARY KEY CLUSTERED (Id),
    Hash varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob_Hash_Status_HeartbeatDateTime ON dbo.ExportJob
(
    Hash,
    Status,
    HeartbeatDateTime
)
