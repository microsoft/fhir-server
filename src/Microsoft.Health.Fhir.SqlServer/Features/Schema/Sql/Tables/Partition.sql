CREATE TABLE dbo.Partition (
    PartitionId         smallint IDENTITY(1,1) NOT NULL,
    PartitionName       varchar(64) NOT NULL,
    IsActive            bit NOT NULL DEFAULT 1,
    CreatedDate         datetimeoffset(7) NOT NULL,
    CONSTRAINT PKC_Partition PRIMARY KEY CLUSTERED (PartitionId),
    CONSTRAINT UQ_Partition_Name UNIQUE (PartitionName)
)