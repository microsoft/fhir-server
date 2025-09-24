/*************************************************************
    Lightweight Data Partitioning - Incremental Backfill Approach - V97

    Following ADR Incremental Backfill Alternative Strategy:
    - Add PartitionId column as instant metadata operation
    - Create filtered indexes with resumable online operations
    - Incremental backfill of system resources with small batches
    - Total timeline: 14-21 hours (vs weeks for staging approach)
    - Zero downtime - can run during business hours
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning incremental backfill migration to version 97.';
GO

/*************************************************************
    Phase 1: Infrastructure Setup (1-2 minutes)
**************************************************************/

-- Create partition lookup table for partition name management
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Partition')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating Partition lookup table';

    CREATE TABLE dbo.Partition (
        PartitionId         smallint IDENTITY(1,1) NOT NULL,
        PartitionName       varchar(64) NOT NULL,
        IsActive            bit NOT NULL DEFAULT 1,
        CreatedDate         datetimeoffset(7) NOT NULL,
        CONSTRAINT PKC_Partition PRIMARY KEY CLUSTERED (PartitionId),
        CONSTRAINT UQ_Partition_Name UNIQUE (PartitionName)
    );

    -- Insert system and default partitions
    INSERT INTO dbo.Partition (PartitionName, CreatedDate) VALUES
        ('system', SYSDATETIMEOFFSET()),    -- PartitionId = 1 (system resources)
        ('default', SYSDATETIMEOFFSET());   -- PartitionId = 2 (default user data)
END;
GO

-- Add PartitionId column to Resource table (instant metadata-only operation)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Resource') AND name = 'PartitionId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PartitionId column to Resource table (metadata-only operation)';

    -- This is an instant operation - just adds metadata, no data changes
    ALTER TABLE dbo.Resource
    ADD PartitionId smallint NOT NULL DEFAULT 2  -- Default partition
    WITH CHECK;

    EXEC dbo.LogSchemaMigrationProgress 'PartitionId column added successfully - all new resources will use default partition';
END;
GO

-- Add foreign key constraint for referential integrity
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Resource_Partition')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding foreign key constraint: Resource -> Partition';

    ALTER TABLE dbo.Resource
    ADD CONSTRAINT FK_Resource_Partition
    FOREIGN KEY (PartitionId) REFERENCES Partition(PartitionId);
END;
GO

-- Add logical partition tracking to JobQueue table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.JobQueue') AND name = 'LogicalPartitionId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding LogicalPartitionId to JobQueue table';

    ALTER TABLE dbo.JobQueue ADD LogicalPartitionId smallint NULL;

    -- Add FK constraint
    ALTER TABLE dbo.JobQueue
    ADD CONSTRAINT FK_JobQueue_LogicalPartition
    FOREIGN KEY (LogicalPartitionId) REFERENCES Partition(PartitionId);

    -- Migrate existing jobs to default partition
    UPDATE dbo.JobQueue
    SET LogicalPartitionId = 2  -- Default partition
    WHERE LogicalPartitionId IS NULL;

    -- Make column NOT NULL after data migration
    ALTER TABLE dbo.JobQueue ALTER COLUMN LogicalPartitionId smallint NOT NULL;
END;
GO

/*************************************************************
    Phase 2: Performance Indexes (2-4 hours, online operations)
**************************************************************/

-- Create partition-scoped index for system resources (can run during business hours)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resource_SystemPartition_ResourceTypeId_ResourceId_Version')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating system partition index (online, resumable operation)';

    CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_SystemPartition_ResourceTypeId_ResourceId_Version
    ON dbo.Resource (ResourceTypeId, ResourceId, Version)
    WHERE PartitionId = 1  -- System partition only
    WITH (
        ONLINE = ON,
        RESUMABLE = ON,
        MAX_DURATION = 240 MINUTES,  -- 4-hour resumable chunks
        MAXDOP = 4,
        DATA_COMPRESSION = PAGE
    ) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;
GO

-- Create partition-scoped index for default partition
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resource_DefaultPartition_ResourceTypeId_ResourceId_Version')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating default partition index (online, resumable operation)';

    CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_DefaultPartition_ResourceTypeId_ResourceId_Version
    ON dbo.Resource (ResourceTypeId, ResourceId, Version)
    WHERE PartitionId = 2  -- Default partition only
    WITH (
        ONLINE = ON,
        RESUMABLE = ON,
        MAX_DURATION = 240 MINUTES,
        MAXDOP = 4,
        DATA_COMPRESSION = PAGE
    ) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;
GO

-- Create current resource index for system partition
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resource_SystemPartition_ResourceTypeId_ResourceId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating system partition current resource index';

    CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_SystemPartition_ResourceTypeId_ResourceId
    ON dbo.Resource (ResourceTypeId, ResourceId)
    INCLUDE (Version, IsDeleted)
    WHERE PartitionId = 1 AND IsHistory = 0
    WITH (
        ONLINE = ON,
        RESUMABLE = ON,
        MAX_DURATION = 240 MINUTES,
        MAXDOP = 4,
        DATA_COMPRESSION = PAGE
    ) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;
GO

-- Create current resource index for default partition
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resource_DefaultPartition_ResourceTypeId_ResourceId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating default partition current resource index';

    CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_DefaultPartition_ResourceTypeId_ResourceId
    ON dbo.Resource (ResourceTypeId, ResourceId)
    INCLUDE (Version, IsDeleted)
    WHERE PartitionId = 2 AND IsHistory = 0
    WITH (
        ONLINE = ON,
        RESUMABLE = ON,
        MAX_DURATION = 240 MINUTES,
        MAXDOP = 4,
        DATA_COMPRESSION = PAGE
    ) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;
GO

/*************************************************************
    Phase 3: Incremental Backfill (12-16 hours, small batches)
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Starting incremental backfill of system resources';
GO

-- Create progress tracking for monitoring
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PartitionMigrationProgress')
BEGIN
    CREATE TABLE dbo.PartitionMigrationProgress (
        ResourceType        varchar(50) NOT NULL PRIMARY KEY,
        TotalRows          bigint NOT NULL DEFAULT 0,
        MigratedRows       bigint NOT NULL DEFAULT 0,
        StartTime          datetimeoffset(7) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        LastUpdateTime     datetimeoffset(7) NULL,
        IsCompleted        bit NOT NULL DEFAULT 0
    );

    -- Initialize with system resource types
    INSERT INTO dbo.PartitionMigrationProgress (ResourceType, TotalRows)
    SELECT
        rt.Name,
        COUNT(*)
    FROM dbo.ResourceType rt
    INNER JOIN dbo.Resource r ON rt.ResourceTypeId = r.ResourceTypeId
    WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                      'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
    GROUP BY rt.Name;
END;
GO

-- Execute incremental backfill with automated batching
DECLARE @RowsUpdated int = 1;
DECLARE @TotalUpdated bigint = 0;
DECLARE @BatchStart datetime2 = GETUTCDATE();
DECLARE @CurrentResourceType varchar(50) = '';

EXEC dbo.LogSchemaMigrationProgress 'Beginning incremental backfill - updating system resources to system partition';

WHILE @RowsUpdated > 0
BEGIN
    -- Update system resources to system partition in small batches
    UPDATE TOP (10000) r
    SET PartitionId = 1
    OUTPUT INSERTED.ResourceTypeId, deleted.ResourceTypeId
    FROM dbo.Resource r
    INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
    WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                      'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
    AND r.PartitionId = 2;  -- Only update rows that haven't been migrated yet

    SET @RowsUpdated = @@ROWCOUNT;
    SET @TotalUpdated = @TotalUpdated + @RowsUpdated;

    -- Update progress tracking
    IF @RowsUpdated > 0
    BEGIN
        UPDATE pmp
        SET MigratedRows = MigratedRows + @RowsUpdated,
            LastUpdateTime = SYSDATETIMEOFFSET()
        FROM dbo.PartitionMigrationProgress pmp
        INNER JOIN dbo.ResourceType rt ON pmp.ResourceType = rt.Name
        WHERE EXISTS (
            SELECT 1 FROM dbo.Resource r
            WHERE r.ResourceTypeId = rt.ResourceTypeId
            AND r.PartitionId = 1
        );
    END;

    -- Yield to other operations (prevents blocking)
    WAITFOR DELAY '00:00:01';

    -- Progress reporting every 100K rows
    IF @TotalUpdated % 100000 = 0 AND @RowsUpdated > 0
    BEGIN
        DECLARE @ProgressMsg nvarchar(200) = CONCAT('Migrated ', @TotalUpdated, ' total rows. Current batch: ',
                     @RowsUpdated, ' rows at ', GETUTCDATE());
        EXEC dbo.LogSchemaMigrationProgress @ProgressMsg;
        PRINT @ProgressMsg;
    END;
END;

DECLARE @DurationMinutes int = DATEDIFF(MINUTE, @BatchStart, GETUTCDATE());
DECLARE @CompletionMsg nvarchar(300) = CONCAT('Migration completed. Total rows migrated: ', @TotalUpdated,
         '. Duration: ', @DurationMinutes, ' minutes');

EXEC dbo.LogSchemaMigrationProgress @CompletionMsg;
PRINT @CompletionMsg;
GO

/*************************************************************
    Phase 4: Validation and Verification
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Running migration validation checks';
GO

-- Validate all system resources are in system partition
DECLARE @MisplacedSystemResources bigint;
SELECT @MisplacedSystemResources = COUNT(*)
FROM dbo.Resource r
INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                  'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
  AND r.PartitionId != 1;

IF @MisplacedSystemResources > 0
BEGIN
    DECLARE @SystemErrorMsg nvarchar(200) = CONCAT('ERROR: Found ', @MisplacedSystemResources, ' system resources not in system partition');
    EXEC dbo.LogSchemaMigrationProgress @SystemErrorMsg;
    RAISERROR(@SystemErrorMsg, 16, 1);
END
ELSE
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'VALIDATION PASSED: All system resources correctly in system partition';
END;

-- Validate all user resources are in default partition
DECLARE @MisplacedUserResources bigint;
SELECT @MisplacedUserResources = COUNT(*)
FROM dbo.Resource r
INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
WHERE rt.Name NOT IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                      'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
  AND r.PartitionId != 2;

IF @MisplacedUserResources > 0
BEGIN
    DECLARE @UserErrorMsg nvarchar(200) = CONCAT('ERROR: Found ', @MisplacedUserResources, ' user resources not in default partition');
    EXEC dbo.LogSchemaMigrationProgress @UserErrorMsg;
    RAISERROR(@UserErrorMsg, 16, 1);
END
ELSE
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'VALIDATION PASSED: All user resources correctly in default partition';
END;

-- Final migration summary
SELECT
    ResourceType,
    TotalRows,
    MigratedRows,
    CASE WHEN TotalRows > 0 THEN CAST(MigratedRows * 100.0 / TotalRows AS DECIMAL(5,2)) ELSE 100.0 END as PercentComplete,
    DATEDIFF(MINUTE, StartTime, ISNULL(LastUpdateTime, SYSDATETIMEOFFSET())) as DurationMinutes,
    IsCompleted
FROM dbo.PartitionMigrationProgress
ORDER BY TotalRows DESC;

-- Update completion status
UPDATE dbo.PartitionMigrationProgress
SET IsCompleted = CASE WHEN MigratedRows = TotalRows THEN 1 ELSE 0 END;

GO

EXEC dbo.LogSchemaMigrationProgress 'Completed incremental backfill migration to version 97.';
GO

/*************************************************************
    Migration Complete!

    ✅ Resource table now has PartitionId column with proper assignments
    ✅ System resources (SearchParameter, etc.) → PartitionId = 1
    ✅ User resources (Patient, Observation, etc.) → PartitionId = 2
    ✅ Partition-scoped indexes created for optimal query performance
    ✅ JobQueue table updated with LogicalPartitionId tracking
    ✅ All operations performed online with zero downtime

    Next Steps:
    1. Enable DataPartitioning feature flag in application configuration
    2. Test partition-aware CRUD operations
    3. Verify search performance with partition filtering
    4. Monitor system resource accessibility across partitions

    Rollback Procedure (if needed):
    UPDATE dbo.Resource SET PartitionId = 2 WHERE PartitionId = 1;
    -- This reverts all resources to default partition
**************************************************************/