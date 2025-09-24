# Staging Table Approach for Lightweight Data Partitioning

*Current ADR 2509 Implementation Strategy*

---

## Executive Summary

This document outlines the **staging table approach** for implementing lightweight data partitioning in the FHIR Server, as specified in ADR 2509. This approach uses sophisticated partition switching techniques to safely migrate hyperscale databases without extended downtime, though it requires significant operational complexity and extended timelines.

## Background

The FHIR Server currently operates as a single-tenant system where all resources share the same logical data space. The staging table approach addresses the challenge of adding partition support to existing hyperscale databases with billions of rows, where direct ALTER TABLE operations could take weeks and pose unacceptable risk.

## Core Strategy: Hyperscale-Safe Migration

### Problem Being Solved
- **Direct ALTER TABLE operations** on billion-row tables could take weeks
- **Index rebuilds** on massive tables risk failure and extended downtime
- **Partition switching** provides millisecond cutover times
- **Resumable operations** allow recovery from failures

### Solution Architecture
Use staging tables with identical schema plus partition columns, migrate data in ResourceType partition chunks, then switch partitions for instant cutover.

## Implementation Phases

### Phase 1: Infrastructure Setup
Create partition lookup table and staging infrastructure:

```sql
/*************************************************************
    Add Data Partitioning Infrastructure
**************************************************************/

-- Create partition lookup table
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
    ('system', SYSDATETIMEOFFSET()),    -- PartitionId = 1
    ('default', SYSDATETIMEOFFSET());   -- PartitionId = 2
```

### Phase 2: Staging Table Creation
Create new schema in parallel to avoid blocking operations:

```sql
-- Create Resource staging table with new partition-aware schema
CREATE TABLE dbo.Resource_Staging (
    PartitionId                 smallint                NOT NULL DEFAULT 2,
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NOT NULL,
    IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0,
    SearchParamHash             varchar(64)             NULL,
    TransactionId               bigint                  NULL,
    HistoryTransactionId        bigint                  NULL,

    CONSTRAINT PKC_Resource_Staging PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) 
        WITH (DATA_COMPRESSION = PAGE)
) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
```

### Phase 3: Partition-by-Partition Migration
Migrate each ResourceType partition individually for safety:

```sql
-- Migration procedure for each ResourceType partition
CREATE OR ALTER PROCEDURE dbo.MigrateResourcePartition
    @ResourceTypeId smallint,
    @BatchSize int = 10000
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProcessedRows int = 0;
    DECLARE @TotalRows int;

    -- Get total rows for this partition
    SELECT @TotalRows = COUNT(*)
    FROM dbo.Resource
    WHERE ResourceTypeId = @ResourceTypeId;

    PRINT 'Starting migration for ResourceTypeId ' + CAST(@ResourceTypeId AS varchar) +
          ' (' + CAST(@TotalRows AS varchar) + ' rows)';

    WHILE @ProcessedRows < @TotalRows
    BEGIN
        -- Migrate batch with PartitionId assignment
        INSERT INTO dbo.Resource_Staging
        SELECT
            CASE
                WHEN rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                               'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
                THEN 1  -- System partition
                ELSE 2  -- Default partition
            END AS PartitionId,
            r.ResourceTypeId,
            r.ResourceId,
            r.Version,
            r.IsHistory,
            r.ResourceSurrogateId,
            r.IsDeleted,
            r.RequestMethod,
            r.RawResource,
            r.IsRawResourceMetaSet,
            r.SearchParamHash,
            r.TransactionId,
            r.HistoryTransactionId
        FROM (
            SELECT TOP (@BatchSize) *
            FROM dbo.Resource r
            WHERE ResourceTypeId = @ResourceTypeId
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.Resource_Staging s
                  WHERE s.ResourceSurrogateId = r.ResourceSurrogateId
              )
            ORDER BY ResourceSurrogateId
        ) r
        INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId;

        SET @ProcessedRows = @ProcessedRows + @@ROWCOUNT;

        -- Progress reporting
        IF @ProcessedRows % 100000 = 0
        BEGIN
            PRINT 'Processed ' + CAST(@ProcessedRows AS varchar) + ' of ' + CAST(@TotalRows AS varchar) +
                  ' rows for ResourceTypeId ' + CAST(@ResourceTypeId AS varchar);
        END;

        -- Yield to other operations
        WAITFOR DELAY '00:00:01';
    END;

    PRINT 'Completed migration for ResourceTypeId ' + CAST(@ResourceTypeId AS varchar);
END;
```

### Phase 4: Resumable Index Creation
Use SQL Server 2019+ resumable operations for large index builds:

```sql
-- Resumable index operations (SQL Server 2019+)
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_Final_PartitionId_ResourceTypeId_ResourceId_Version
ON dbo.Resource_Staging (PartitionId, ResourceTypeId, ResourceId, Version)
WITH (
    ONLINE = ON,
    RESUMABLE = ON,
    MAX_DURATION = 240 MINUTES,  -- 4 hour chunks
    MAXDOP = 4,
    DATA_COMPRESSION = PAGE
) ON PartitionScheme_ResourceTypeId(ResourceTypeId);

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_Final_PartitionId_ResourceTypeId_ResourceId
ON dbo.Resource_Staging (PartitionId, ResourceTypeId, ResourceId)
INCLUDE (Version, IsDeleted)
WHERE IsHistory = 0
WITH (
    ONLINE = ON,
    RESUMABLE = ON,
    MAX_DURATION = 240 MINUTES,
    MAXDOP = 4,
    DATA_COMPRESSION = PAGE
) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
```

### Phase 5: Partition Switching (Minimal Downtime)
Execute the actual cutover using partition switching for instant operations:

```sql
-- Partition switching strategy - milliseconds operation per partition
-- Example for switching ResourceTypeId = 1 partition
BEGIN TRANSACTION;

    -- Verify data integrity before switching
    IF (SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = 1) =
       (SELECT COUNT(*) FROM dbo.Resource_Staging WHERE ResourceTypeId = 1)
    BEGIN
        -- Switch partitions (milliseconds operation)
        ALTER TABLE dbo.Resource SWITCH PARTITION 1 TO dbo.Resource_Old PARTITION 1;
        ALTER TABLE dbo.Resource_Staging SWITCH PARTITION 1 TO dbo.Resource PARTITION 1;

        PRINT 'Successfully switched partition for ResourceTypeId = 1';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('Data validation failed for ResourceTypeId = 1', 16, 1);
    END;

COMMIT TRANSACTION;
```

## Migration Timeline and Performance

### Scale Assumptions (Large Hyperscale Database)
- **1 billion resources** in Resource table
- **~150 ResourceType partitions** to migrate
- **Hyperscale tier**: 80+ vCores
- **Existing partitioning**: By ResourceTypeId

### Detailed Timeline

| Phase | Operation | Duration | Complexity | Risk Level |
|-------|-----------|----------|------------|------------|
| **Phase 1** | Infrastructure setup | 1-2 hours | Medium | Low |
| **Phase 2** | Staging table creation | 2-4 hours | Medium | Low |
| **Phase 3** | Data migration | **2-6 weeks** | Very High | High |
| **Phase 4** | Index creation | 1-3 days | High | Medium |
| **Phase 5** | Partition switching | 4-8 hours | Very High | Very High |
| **Total** | Complete migration | **3-7 weeks** | **Very High** | **High** |

### Phase 3 Breakdown (Most Critical)
```sql
-- Migration execution strategy per ResourceType
-- Largest partitions (e.g., Patient, Observation) could have 100M+ rows
-- 100M rows ÷ 10K batch size = 10,000 batches
-- 10,000 batches × 5 seconds per batch = ~14 hours per large partition
-- Multiple large partitions × 14 hours = weeks of migration time
```

### Resource Requirements
- **Storage**: 2x current database size (staging tables)
- **Compute**: High CPU utilization during migration phases
- **Memory**: Significant buffer pool requirements for dual schemas
- **I/O**: Heavy read/write operations during data movement

## Operational Complexity

### Migration Coordination
```sql
-- Master migration orchestration procedure
CREATE OR ALTER PROCEDURE dbo.ExecuteFullPartitionMigration
AS
BEGIN
    DECLARE @ResourceTypeId smallint;
    DECLARE @StartTime datetime2 = GETUTCDATE();
    
    -- Get all ResourceTypes ordered by size (smallest first)
    DECLARE ResourceType_Cursor CURSOR FOR
    SELECT rt.ResourceTypeId
    FROM dbo.ResourceType rt
    INNER JOIN (
        SELECT ResourceTypeId, COUNT(*) as RowCount
        FROM dbo.Resource
        GROUP BY ResourceTypeId
    ) rc ON rt.ResourceTypeId = rc.ResourceTypeId
    ORDER BY rc.RowCount; -- Start with smallest partitions
    
    OPEN ResourceType_Cursor;
    FETCH NEXT FROM ResourceType_Cursor INTO @ResourceTypeId;
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        PRINT 'Starting migration for ResourceTypeId: ' + CAST(@ResourceTypeId AS varchar);
        
        -- Execute partition migration
        EXEC dbo.MigrateResourcePartition @ResourceTypeId = @ResourceTypeId;
        
        -- Validate migration results
        EXEC dbo.ValidatePartitionMigration @ResourceTypeId = @ResourceTypeId;
        
        FETCH NEXT FROM ResourceType_Cursor INTO @ResourceTypeId;
    END;
    
    CLOSE ResourceType_Cursor;
    DEALLOCATE ResourceType_Cursor;
    
    PRINT 'Full migration completed in ' + 
          CAST(DATEDIFF(HOUR, @StartTime, GETUTCDATE()) AS varchar) + ' hours';
END;
```

### Monitoring and Validation
```sql
-- Comprehensive validation queries
CREATE OR ALTER PROCEDURE dbo.ValidatePartitionMigration
    @ResourceTypeId smallint = NULL
AS
BEGIN
    -- 1. Row count validation
    IF @ResourceTypeId IS NOT NULL
    BEGIN
        DECLARE @OriginalCount bigint, @StagingCount bigint;
        
        SELECT @OriginalCount = COUNT(*)
        FROM dbo.Resource
        WHERE ResourceTypeId = @ResourceTypeId;
        
        SELECT @StagingCount = COUNT(*)
        FROM dbo.Resource_Staging
        WHERE ResourceTypeId = @ResourceTypeId;
        
        IF @OriginalCount != @StagingCount
        BEGIN
            RAISERROR('Row count mismatch for ResourceTypeId %d: Original=%d, Staging=%d', 
                      16, 1, @ResourceTypeId, @OriginalCount, @StagingCount);
            RETURN;
        END;
    END;
    
    -- 2. Partition assignment validation
    SELECT 'System Resource Validation' as ValidationStage,
           COUNT(*) as MisplacedSystemResources
    FROM dbo.Resource_Staging rs
    INNER JOIN dbo.ResourceType rt ON rs.ResourceTypeId = rt.ResourceTypeId
    WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                      'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
      AND rs.PartitionId != 1;  -- Should be 0
    
    SELECT 'User Resource Validation' as ValidationStage,
           COUNT(*) as MisplacedUserResources
    FROM dbo.Resource_Staging rs
    INNER JOIN dbo.ResourceType rt ON rs.ResourceTypeId = rt.ResourceTypeId
    WHERE rt.Name NOT IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                          'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
      AND rs.PartitionId != 2;  -- Should be 0
    
    -- 3. Data integrity validation
    SELECT 'Data Integrity Check' as ValidationStage,
           COUNT(*) as CorruptedResources
    FROM dbo.Resource_Staging
    WHERE RawResource IS NULL OR LEN(RawResource) = 0;  -- Should be 0
    
    PRINT 'Validation completed for ResourceTypeId: ' + ISNULL(CAST(@ResourceTypeId AS varchar), 'ALL');
END;
```

## Risk Management

### High-Risk Factors
- ❌ **Complex partition switching** - requires perfect coordination
- ❌ **Extended timeline** - weeks of migration increase failure probability
- ❌ **Storage requirements** - 2x database size needed
- ❌ **Rollback complexity** - difficult to reverse partition switches
- ❌ **Coordination complexity** - multiple phases must align perfectly

### Risk Mitigation Strategies

#### 1. Hyperscale-Specific Optimizations
```sql
-- Leverage existing PartitionScheme_ResourceTypeId for safety
-- Migrate each ResourceType partition independently
-- Use staging table approach to avoid long-running ALTER operations
-- Implement resumable operations with MAX_DURATION limits
-- Use batch processing with progress monitoring and yield points
```

#### 2. Rollback Strategy
```sql
-- Complex rollback procedure for partition switching
CREATE OR ALTER PROCEDURE dbo.RollbackPartitionMigration
    @ResourceTypeId smallint
AS
BEGIN
    BEGIN TRANSACTION;
    
    -- Attempt to switch back from staging
    ALTER TABLE dbo.Resource SWITCH PARTITION @ResourceTypeId TO dbo.Resource_Rollback PARTITION @ResourceTypeId;
    ALTER TABLE dbo.Resource_Original SWITCH PARTITION @ResourceTypeId TO dbo.Resource PARTITION @ResourceTypeId;
    
    -- Validate rollback success
    DECLARE @OriginalCount bigint, @CurrentCount bigint;
    SELECT @OriginalCount = COUNT(*) FROM dbo.Resource_Original WHERE ResourceTypeId = @ResourceTypeId;
    SELECT @CurrentCount = COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId;
    
    IF @OriginalCount = @CurrentCount
    BEGIN
        COMMIT TRANSACTION;
        PRINT 'Rollback successful for ResourceTypeId: ' + CAST(@ResourceTypeId AS varchar);
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('Rollback failed for ResourceTypeId %d', 16, 1, @ResourceTypeId);
    END;
END;
```

#### 3. Testing Strategy
```sql
-- Comprehensive testing infrastructure
CREATE OR ALTER PROCEDURE dbo.TestPartitionMigration
    @TestResourceTypeId smallint = 1  -- Start with smallest partition
AS
BEGIN
    -- Create test staging environment
    CREATE TABLE dbo.Resource_Test AS SELECT TOP 0 * FROM dbo.Resource_Staging;
    
    -- Test migration on small subset
    INSERT INTO dbo.Resource_Test
    SELECT TOP 1000 * FROM dbo.Resource WHERE ResourceTypeId = @TestResourceTypeId;
    
    -- Validate test results
    EXEC dbo.ValidatePartitionMigration @ResourceTypeId = @TestResourceTypeId;
    
    -- Cleanup test environment
    DROP TABLE dbo.Resource_Test;
    
    PRINT 'Test migration completed successfully';
END;
```

## JobQueue Integration
Handle existing JobQueue partitioning conflicts:

```sql
-- Add logical partition tracking to existing JobQueue
ALTER TABLE dbo.JobQueue ADD LogicalPartitionId smallint NULL;
ALTER TABLE dbo.JobQueue ADD CONSTRAINT FK_JobQueue_LogicalPartition
    FOREIGN KEY (LogicalPartitionId) REFERENCES Partition(PartitionId);

-- Migrate existing jobs to 'default' partition
UPDATE dbo.JobQueue SET LogicalPartitionId = 2 WHERE LogicalPartitionId IS NULL;
ALTER TABLE dbo.JobQueue ALTER COLUMN LogicalPartitionId smallint NOT NULL;
```

## Advantages of Staging Table Approach

### ✅ Theoretical Benefits
- **Hyperscale Safety**: Designed for databases with billions of rows
- **Partition Switching**: Millisecond cutover times per partition
- **Resumable Operations**: Can recover from failures mid-process
- **Data Integrity**: Multiple validation checkpoints
- **Parallel Processing**: Can migrate multiple partitions simultaneously

### ✅ Enterprise-Grade Features
- **Comprehensive Logging**: Full audit trail of migration process
- **Rollback Capabilities**: Can reverse operations if needed
- **Testing Framework**: Validate approach on subsets first
- **Progress Monitoring**: Detailed visibility into migration status

## Disadvantages and Challenges

### ❌ Operational Complexity
- **Very High Complexity**: Requires deep SQL Server expertise
- **Extended Timeline**: 3-7 weeks total migration time
- **Storage Requirements**: 2x database size needed during migration
- **Coordination Overhead**: Multiple phases must execute perfectly

### ❌ Risk Factors
- **High Failure Probability**: Many complex operations increase risk
- **Difficult Testing**: Complex to test thoroughly in production-like environment
- **Rollback Complexity**: Reversing partition switches is complex
- **Extended Maintenance Windows**: Some operations require downtime

### ❌ Resource Requirements
- **Skilled Personnel**: Requires database experts for execution
- **Additional Storage**: Significant temporary storage overhead
- **Performance Impact**: Migration operations affect database performance
- **Monitoring Overhead**: Requires constant supervision during migration

## Conclusion

The staging table approach represents a **theoretically sound but operationally complex** solution for implementing lightweight data partitioning in hyperscale FHIR Server deployments. While it addresses the technical challenges of migrating billion-row tables, it comes with significant operational overhead and extended timelines.

### Key Characteristics:
- ✅ **Hyperscale-safe** design
- ✅ **Minimal cutover downtime** (partition switching)
- ✅ **Comprehensive validation** and rollback capabilities
- ❌ **Very high complexity** and operational overhead
- ❌ **Extended timeline** (3-7 weeks)
- ❌ **High resource requirements** (storage, expertise, coordination)

This approach is best suited for organizations with:
- **Very large hyperscale databases** (multi-billion rows)
- **Deep SQL Server expertise** available
- **Tolerance for extended migration timelines**
- **Ability to dedicate significant resources** to the migration project

For many deployments, simpler alternatives may provide better balance of functionality, timeline, and operational risk.

---

## Appendix: Migration Execution Checklist

### Pre-Migration (1-2 weeks)
- [ ] Verify SQL Server version supports resumable operations
- [ ] Confirm available storage (2x current database size)
- [ ] Test migration procedures on smaller databases
- [ ] Coordinate maintenance windows with business stakeholders
- [ ] Prepare rollback procedures and test scenarios
- [ ] Set up comprehensive monitoring and alerting

### Migration Execution (3-7 weeks)
- [ ] **Week 1**: Phase 1-2 (Infrastructure and staging setup)
- [ ] **Weeks 2-5**: Phase 3 (Data migration, ResourceType by ResourceType)
- [ ] **Week 6**: Phase 4 (Index creation with resumable operations)
- [ ] **Week 7**: Phase 5 (Partition switching and validation)

### Post-Migration (1 week)
- [ ] Validate all data integrity checks
- [ ] Update application configuration to enable partitioning
- [ ] Monitor performance and query execution plans
- [ ] Clean up staging tables and temporary objects
- [ ] Document lessons learned and operational procedures
- [ ] Update backup/restore procedures for partition awareness