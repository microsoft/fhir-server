/* Staging table that will be used for partition switch out. */
CREATE TABLE dbo.ResourceChangeDataStaging 
(
    Id bigint IDENTITY(1,1) NOT NULL,
    Timestamp datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeDataStaging_Timestamp DEFAULT sysutcdatetime(),
    ResourceId varchar(64) NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceVersion int NOT NULL,
    ResourceChangeTypeId tinyint NOT NULL,
    PartitionDatetime datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeDataStaging_PartitionDatetime DEFAULT (DATEADD(HOUR,DATEDIFF(HOUR,0,SYSUTCDATETIME()),0)),
    CONSTRAINT PK_ResourceChangeDataStaging_PartitionDatetimeId PRIMARY KEY (PartitionDatetime ASC, Id ASC)
) ON [PRIMARY]

/* Adds a check constraint on the staging table for a partition boundary validation. */
ALTER TABLE dbo.ResourceChangeDataStaging WITH CHECK 
    ADD CONSTRAINT CHK_ResourceChangeDataStaging_partition CHECK(PartitionDatetime < CONVERT(DATETIME2(7), N'9999-12-31 23:59:59.9999999'));

ALTER TABLE dbo.ResourceChangeDataStaging CHECK CONSTRAINT CHK_ResourceChangeDataStaging_partition;
