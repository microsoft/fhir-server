/*************************************************************
    Export Job
**************************************************************/
CREATE TABLE dbo.ExportJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Hash varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ExportJob ON dbo.ExportJob
(
    Id
)

CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob_Hash_Status_HeartbeatDateTime ON dbo.ExportJob
(
    Hash,
    Status,
    HeartbeatDateTime
)
