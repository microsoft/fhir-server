/*************************************************************
    This migration removes the existing primary key clustered index and adds a clustered index on Id column in the ResourceChangeData table.
    The migration is "online" meaning the server is fully available during the upgrade, but it can be very time-consuming.
    For reference, a resource change data table with 10 million records took around 25 minutes to complete 
    on the Azure SQL database (SQL elastic pools - GeneralPurpose: Gen5, 2 vCores).
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 24.';
GO

EXEC dbo.LogSchemaMigrationProgress 'Adding or updating FetchResourceChanges_3 stored procedure.';
GO

--
-- STORED PROCEDURE
--     FetchResourceChanges_3
--
-- DESCRIPTION
--     Returns the number of resource change records from startId. The start id is inclusive.
--
-- PARAMETERS
--     @startId
--         * The start id of resource change records to fetch.
--     @lastProcessedUtcDateTime
--         * The last checkpoint datetime in UTC time (Coordinated Universal Time).
--     @pageSize
--         * The page size for fetching resource change records.
--
-- RETURN VALUE
--     Resource change data rows.
--
CREATE OR ALTER PROCEDURE dbo.FetchResourceChanges_3
    @startId bigint,
    @lastProcessedUtcDateTime datetime2(7),
    @pageSize smallint
AS
BEGIN
   
    SET NOCOUNT ON;

    /* Finds the prior partition to the current partition where the last processed watermark lies. It is a normal scenario when a prior watermark exists. */
    DECLARE @precedingPartitionBoundary AS datetime2(7) = (SELECT TOP(1) CAST(prv.value as datetime2(7)) AS value FROM sys.partition_range_values AS prv WITH (NOLOCK)
                                                            INNER JOIN sys.partition_functions AS pf WITH (NOLOCK) ON pf.function_id = prv.function_id
                                                        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                            AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                            AND CAST(prv.value AS datetime2(7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                        ORDER BY prv.boundary_id DESC);

    IF (@precedingPartitionBoundary IS NULL)
    BEGIN
        /* It happens when no prior watermark exists or the last processed datetime of prior watermark is older than the last retention datetime.
           Uses the partition anchor datetime as the last processed DateTime. */
        SET @precedingPartitionBoundary = CONVERT (DATETIME2 (7), N'1970-01-01T00:00:00.0000000');
    END;

    /* It ensures that it will not check resource changes in future partitions. */
    DECLARE @endDateTimeToFilter AS datetime2(7) = DATEADD(HOUR, 1, SYSUTCDATETIME());

    WITH PartitionBoundaries
    AS (
        /* Normal logic when prior watermark exists, grab prior partition to ensure we do not miss data that was written across a partition boundary,
           and includes partitions until current one to ensure we keep moving to the next partition when some partitions do not have any resource changes. */
        SELECT CAST(prv.value as datetime2(7)) AS PartitionBoundary FROM sys.partition_range_values AS prv WITH (NOLOCK)
            INNER JOIN sys.partition_functions AS pf WITH (NOLOCK) ON pf.function_id = prv.function_id
        WHERE pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
            AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
            AND CAST(prv.value AS datetime2(7)) BETWEEN @precedingPartitionBoundary AND @endDateTimeToFilter
    )
    SELECT TOP(@pageSize) Id,
        Timestamp,
        ResourceId,
        ResourceTypeId,
        ResourceVersion,
        ResourceChangeTypeId
    FROM PartitionBoundaries AS p
    CROSS APPLY (
        /* Given the fact that Read Committed Snapshot isolation level is enabled on the FHIR database,
           using TABLOCK and HOLDLOCK table hints to avoid skipping resource changes 
           due to interleaved transactions on the resource change data table. */
        SELECT TOP(@pageSize) Id,
            Timestamp,
            ResourceId,
            ResourceTypeId,
            ResourceVersion,
            ResourceChangeTypeId
        /* Acquires and holds the table lock to prevent new resource changes from being created during the select query execution.
           Without the TABLOCK and HOLDLOCK hints, the lock will be applied at the row or page level which can result in missed reads
           if an insert occurs in range that was previously read and the locks were released on the read. */
        FROM dbo.ResourceChangeData WITH (TABLOCK, HOLDLOCK)
            WHERE Id >= @startId
                AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(p.PartitionBoundary)
        ORDER BY Id ASC
        ) AS rcd
    ORDER BY rcd.Id ASC;
END;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'PK_ResourceChangeData_TimestampId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Deleting PK_ResourceChangeData_TimestampId index from ResourceChangeData table.';

    /* Drops PK_ResourceChangeData_TimestampId index. "ONLINE = ON" indicates long-term table locks aren't held for the duration of the index operation. 
       During the main phase of the index operation, only an Intent Share (IS) lock is held on the source table. 
       This behavior enables queries or updates to the underlying table and indexes to continue. */
    ALTER TABLE dbo.ResourceChangeData DROP CONSTRAINT PK_ResourceChangeData_TimestampId WITH(ONLINE = ON);
END;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'PK_ResourceChangeDataStaging_TimestampId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Deleting PK_ResourceChangeDataStaging_TimestampId index from ResourceChangeDataStaging table.';

    /* Drops PK_ResourceChangeDataStaging_TimestampId index. "ONLINE = ON" indicates long-term table locks aren't held for the duration of the index operation. 
       During the main phase of the index operation, only an Intent Share (IS) lock is held on the source table. 
       This behavior enables queries or updates to the underlying table and indexes to continue. */
    ALTER TABLE dbo.ResourceChangeDataStaging DROP CONSTRAINT PK_ResourceChangeDataStaging_TimestampId WITH(ONLINE = ON);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IXC_ResourceChangeData')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IXC_ResourceChangeData index on ResourceChangeData table.';

    /* Adds a clustered index on ResourceChangeData table. "ONLINE = ON" indicates long-term table locks aren't held for the duration of the index operation. 
       During the main phase of the index operation, only an Intent Share (IS) lock is held on the source table. 
       This behavior enables queries or updates to the underlying table and indexes to continue. 
       Creating a non-primary key and non-unique clustered index to have a better performance on the fetch query.
       Since a resourceChangeData table is partitioned on timestamp, we can not create the primary key only on the Id column
       due to a SQL constraint, "partition columns for a unique index must be a subset of the index key".
       Also, we don't want to include the partitioning timestamp column on the index due to a skipping record issue related to ordering by timestamp.
       Previously, the uniqueness was combined with the timestamp column and it was only per partition. 
       To enforce global uniqueness requires a non clustered index without a partition but which prevents partition swaps.
       We are using identity which will guarantee uniqueness unless an identity insert is used or reseed identity value on the table which shouldn't happen. */
    CREATE CLUSTERED INDEX IXC_ResourceChangeData ON dbo.ResourceChangeData
        (Id ASC) WITH(ONLINE = ON) ON PartitionScheme_ResourceChangeData_Timestamp(Timestamp);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IXC_ResourceChangeDataStaging')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IXC_ResourceChangeDataStaging index on ResourceChangeDataStaging table.';

    /* Adds a clustered index on ResourceChangeDataStaging table. "ONLINE = ON" indicates long-term table locks aren't held for the duration of the index operation. 
       During the main phase of the index operation, only an Intent Share (IS) lock is held on the source table. 
       This behavior enables queries or updates to the underlying table and indexes to continue. */
    CREATE CLUSTERED INDEX IXC_ResourceChangeDataStaging ON dbo.ResourceChangeDataStaging
        (Id ASC, Timestamp ASC) WITH(ONLINE = ON) ON [PRIMARY];
END;
GO

EXEC dbo.LogSchemaMigrationProgress 'Completed migration to version 24.';
GO
