/* Partitioned table that stores resource change information. */
CREATE TABLE dbo.ResourceChangeData 
(
    Id bigint IDENTITY(1,1) NOT NULL,
    Timestamp datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeData_Timestamp DEFAULT sysutcdatetime(),
    ResourceId varchar(64) NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceVersion int NOT NULL,
    ResourceChangeTypeId tinyint NOT NULL,
    PartitionDatetime datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeData_PartitionDatetime DEFAULT (DATEADD(HOUR,DATEDIFF(HOUR,0,SYSUTCDATETIME()),0)),
    CONSTRAINT PK_ResourceChangeData_PartitionDatetimeId PRIMARY KEY (PartitionDatetime ASC, Id ASC)
) ON PartitionScheme_ResourceChangeData_Timestamp(PartitionDatetime)
