/* Partitioned table that stores resource change information. */
CREATE TABLE dbo.ResourceChangeData 
(
    Id bigint IDENTITY(1,1) NOT NULL,
    Timestamp datetime2(7) NOT NULL CONSTRAINT DF_ResourceChangeData_Timestamp DEFAULT sysutcdatetime(),
    ResourceId varchar(64) NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceVersion int NOT NULL,
    ResourceChangeTypeId tinyint NOT NULL
) ON PartitionScheme_ResourceChangeData_Timestamp(Timestamp);

/* Creating a non-primary key and non-unique clustered index to have a better performance on the fetch query.
   Since a resourceChangeData table is partitioned on timestamp, we can not create the primary key only on the Id column
   due to a SQL constraint, "partition columns for a unique index must be a subset of the index key".
   Also, we don't want to include the partitioning timestamp column on the index due to a skipping record issue related to ordering by timestamp.
   Previously, the uniqueness was combined with the timestamp column and it was only per partition. 
   To enforce global uniqueness requires a non clustered index without a partition but which prevents partition swaps.
   We are using identity which will guarantee uniqueness unless an identity insert is used or reseed identity value on the table which shouldn't happen. */
CREATE CLUSTERED INDEX IXC_ResourceChangeData ON dbo.ResourceChangeData
    (Id ASC) WITH(ONLINE = ON) ON PartitionScheme_ResourceChangeData_Timestamp(Timestamp);
