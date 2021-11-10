/* Partitioned table that stores resource change information. */
CREATE TABLE dbo.ResourceChangeData 
(
    Id bigint IDENTITY(1,1) NOT NULL,
    Timestamp datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeData_Timestamp DEFAULT sysutcdatetime(),
    ResourceId varchar(64) NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceVersion int NOT NULL,
    ResourceChangeTypeId tinyint NOT NULL,
    CONSTRAINT PK_ResourceChangeData_TimestampId PRIMARY KEY (Timestamp, Id)
) ON PartitionScheme_ResourceChangeData_Timestamp(Timestamp)
