# ADR 2509: Lightweight Data Partitioning for FHIR Server
*Labels*: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL) | [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [Multi-tenant](https://github.com/microsoft/fhir-server/labels/Area-Multi-tenant)

---

## Context

### Problem Statement
The FHIR Server currently operates as a single-tenant system where all resources share the same logical data space. As customer requirements evolve toward multi-tenant scenarios and data migration use cases, we need to introduce data partitioning capabilities similar to Azure DICOM's approach. The goal is to provide logical data isolation while maintaining FHIR compliance, operational efficiency, and performance characteristics.

### Current Architecture
The FHIR Server's core `Resource` table structure includes:
- Primary keys: `ResourceTypeId`, `ResourceSurrogateId`
- Unique constraints: `(ResourceTypeId, ResourceId, Version)` and `(ResourceTypeId, ResourceId)` for current versions
- Partitioned by `ResourceTypeId` using `PartitionScheme_ResourceTypeId`
- Search parameter tables (`StringSearchParam`, `TokenSearchParam`, etc.) linked via `ResourceSurrogateId`

### Requirements
- Logical data isolation between partitions
- Resource ID uniqueness per partition (not globally)
- Minimal changes to existing FHIR API surface
- Maintain performance chBuaracteristics
- Preserve existing single-tenant behavior as default
- SQL Server implementation only (Cosmos DB unaffected)

### Azure DICOM Reference Model
Azure DICOM implements partitioning through:
- Partition identifiers in URL path: `/partitions/{partitionName}`
- Partition constraints: 64 characters, alphanumeric + `.`, `-`, `_`
- No cross-partition querying
- Implicit partition creation
- Default partition (`Microsoft.Default`) for existing data

## Decision

We will implement **lightweight logical data partitioning** for SQL Server deployments only, using an efficient partition lookup table approach with feature flag control. This provides logical data separation without changing the existing physical partitioning scheme (PartitionScheme_ResourceTypeId).

### Core Database Schema Changes

#### 1. Partition Lookup Table
```sql
CREATE TABLE dbo.Partition (
    PartitionId         smallint IDENTITY(1,1) NOT NULL,
    PartitionName       varchar(64) NOT NULL,
    IsActive            bit NOT NULL DEFAULT 1,
    CreatedDate         datetimeoffset(7) NOT NULL,
    CONSTRAINT PKC_Partition PRIMARY KEY CLUSTERED (PartitionId),
    CONSTRAINT UQ_Partition_Name UNIQUE (PartitionName)
)

-- Insert default partition
INSERT INTO dbo.Partition (PartitionName, CreatedDate)
VALUES ('default', SYSDATETIMEOFFSET())
```

#### 2. Resource Table Enhancement
```sql
-- Add partition column (2 bytes vs 128 bytes for varchar(64))
ALTER TABLE dbo.Resource
ADD PartitionId smallint NOT NULL DEFAULT 1 -- Default partition

-- Update unique constraints to be partition-scoped
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_PartitionId_ResourceTypeId_ResourceId_Version
ON dbo.Resource (PartitionId, ResourceTypeId, ResourceId, Version)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_PartitionId_ResourceTypeId_ResourceId
ON dbo.Resource (PartitionId, ResourceTypeId, ResourceId)
INCLUDE (Version, IsDeleted)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
```

#### 3. Search Parameter Tables
**No PartitionId column needed** - Search parameter tables join to Resource via globally unique ResourceSurrogateId, so Resource table partition filtering automatically constrains search results. This provides significant storage and index efficiency benefits.

**Note**: This is logical partitioning only - the existing physical PartitionScheme_ResourceTypeId remains unchanged and continues to provide physical data distribution by ResourceType.

### API Route Integration via Middleware
**Middleware-Based URL Rewriting Approach:**
- `PartitionRoutingMiddleware` intercepts requests with `/partitions/{name}/...` pattern
- Extracts partition name and stores in `IFhirRequestContext`
- Rewrites URL by removing `/partitions/{name}` prefix before routing
- Downstream controllers and routes remain completely unchanged
- Non-partitioned requests automatically get 'default' partition context

**Route Examples:**
- `/partitions/tenant-a/Patient` → middleware extracts `tenant-a`, rewrites to `/Patient`
- `/Patient` → middleware sets 'default' partition, passes through unchanged
- All existing MVC routes continue working without modification

### Feature Flag Implementation

**Configuration Strategy - Enable Anytime, Never Disable:**

#### Phase 1: Deploy Partition-Aware Application Code
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": false
      }
    }
  }
}
```

**Application behavior:**
- Detects current schema version on startup
- Routes non-partitioned requests to 'default' partition logic when schema migrated
- Ready to handle partitioned requests once feature enabled

#### Phase 2: Schema Migration (Can Run Anytime)
```sql
-- Add partition support to existing data (online operation)
CREATE TABLE dbo.Partition (
    PartitionId smallint IDENTITY(1,1) NOT NULL,
    PartitionName varchar(64) NOT NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedDate datetimeoffset(7) NOT NULL,
    CONSTRAINT PKC_Partition PRIMARY KEY CLUSTERED (PartitionId),
    CONSTRAINT UQ_Partition_Name UNIQUE (PartitionName)
);

INSERT INTO dbo.Partition (PartitionName, CreatedDate)
VALUES ('default', SYSDATETIMEOFFSET());

ALTER TABLE dbo.Resource ADD PartitionId smallint NOT NULL DEFAULT 1;

-- Rebuild indexes to include PartitionId (online operation in SQL Server)
-- All existing data automatically in 'default' partition
```

#### Phase 3: Enable Feature Flag (Permanent)
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": true,
        "DefaultPartition": "default"
      }
    }
  }
}
```

**Post-enablement behavior:**
- Partitioned endpoints become available: `/partitions/{name}/...`
- Non-partitioned endpoints continue working (route to 'default')
- New partitions created implicitly on first access
- **Cannot be disabled** - flag becomes permanent architectural decision

**Why "Never Disable":**
- Once data exists in multiple partitions, consolidation creates Resource ID conflicts
- Partition-scoped application logic becomes dependent on partitioned architecture
- Feature becomes permanent architectural decision

```csharp
public class SqlQueryGenerator
{
    protected virtual string GetPartitionFilter(string resourceType)
    {
        if (SystemResourceTypes.Types.Contains(resourceType))
        {
            // System resources: always accessible from 'system' partition regardless of current context
            return "AND r.PartitionId = @systemPartitionId";
        }
        else
        {
            // Regular resources: filter by current partition context
            return "AND r.PartitionId = @currentPartitionId";
        }
    }

    // Note: No partition join condition needed for search param tables
    // ResourceSurrogateId join is sufficient since it's globally unique
}
```

### Complete FHIR API Per Partition Architecture

**Every partition provides the full FHIR API surface:**

```
# Tenant A - Complete FHIR API
/partitions/tenant-a/metadata → CapabilityStatement (shared data, appears tenant-scoped)
/partitions/tenant-a/SearchParameter → All SearchParameters (shared data, appears tenant-scoped)
/partitions/tenant-a/OperationDefinition → All OpDefs (shared data, appears tenant-scoped)
/partitions/tenant-a/Patient → Patient resources (truly tenant-scoped data)
/partitions/tenant-a/$export → Export job (tenant-scoped operation)
/partitions/tenant-a/$reindex → Reindex job (appears tenant-scoped, runs server-wide)

# Tenant B - Same Complete FHIR API
/partitions/tenant-b/metadata → Same CapabilityStatement (shared data, appears tenant-scoped)
/partitions/tenant-b/SearchParameter → Same SearchParameters (shared data, appears tenant-scoped)
/partitions/tenant-b/OperationDefinition → Same OpDefs (shared data, appears tenant-scoped)
/partitions/tenant-b/Patient → Patient resources (truly tenant-scoped data)
/partitions/tenant-b/$export → Export job (tenant-scoped operation)
```

**System Resource Types (Hardcoded):**
```csharp
public static class SystemResourceTypes
{
    public static readonly HashSet<string> Types = new()
    {
        "SearchParameter", "OperationDefinition", "StructureDefinition",
        "ValueSet", "CodeSystem", "CapabilityStatement", "CompartmentDefinition"
    };
}
```

**Request Context and Filtering Logic:**
```csharp
public class FhirRequestContext
{
    public int CurrentPartitionId { get; set; }    // From URL: 'default'=2, 'tenant-a'=3, etc.
    public int SystemPartitionId { get; set; } = 1; // Always 1 for 'system' partition

    public int GetPartitionIdForResource(string resourceType)
    {
        return SystemResourceTypes.Types.Contains(resourceType)
            ? SystemPartitionId      // System resources always from 'system' partition
            : CurrentPartitionId;    // Regular resources from current partition context
    }
}

public class PartitionedResourceHandler
{
    public async Task<IActionResult> HandleResourceRequest(
        string partitionName,
        string resourceType,
        string resourceId)
    {
        var context = new FhirRequestContext
        {
            CurrentPartitionId = await GetPartitionId(partitionName)
        };

        if (SystemResourceTypes.Types.Contains(resourceType))
        {
            // System resources: always from 'system' partition but present as tenant-scoped
            var resource = await GetResource(context.SystemPartitionId, resourceType, resourceId);
            if (resource != null)
            {
                ModifyResourceUrlsForPartition(resource, partitionName);
                return Ok(resource);
            }
            return NotFound();
        }
        else
        {
            // Regular resources: from current partition context
            var resource = await GetResource(context.CurrentPartitionId, resourceType, resourceId);
            return resource != null ? Ok(resource) : NotFound();
        }
    }
}
```

**Benefits of Complete API Per Partition:**
- **Perfect Tenant Isolation** - Tenants never access anything outside their partition URL space
- **No Special Routing** - Single pattern: `/partitions/{name}/{fhir-path}`
- **Complete FHIR Functionality** - Every partition has full metadata, operations, system resources
- **Clean Security Boundaries** - All access is partition-scoped, no cross-partition references needed

**Implementation Details:**
- System resource sharing is an implementation detail, not visible in API
- SearchParameter definitions managed by singleton `SearchParameterDefinitionManager`
- Search indexer continues as singleton processing all partitions
- System resources appear partition-scoped but are efficiently shared behind the scenes

### Query Generation Impact
All SQL queries require partition context awareness:
```sql
-- Simple resource query by type
SELECT * FROM Resource r
WHERE r.PartitionId = @currentPartitionId AND r.ResourceTypeId = @resourceTypeId AND r.ResourceId = @resourceId

-- System resource query (always from 'system' partition)
SELECT * FROM Resource r
WHERE r.PartitionId = @systemPartitionId AND r.ResourceTypeId = @resourceTypeId AND r.ResourceId = @resourceId

-- Search with _include (accesses both current partition and system resources)
SELECT r.* FROM Resource r
INNER JOIN StringSearchParam s ON r.ResourceSurrogateId = s.ResourceSurrogateId
WHERE (
    -- Regular resources from current partition
    r.PartitionId = @currentPartitionId
    OR
    -- System resources always accessible
    (r.PartitionId = @systemPartitionId AND r.ResourceTypeId IN (@systemResourceTypeIds))
)
AND s.SearchParamId = @searchParamId AND s.Text = @searchValue

-- Chained search example: Patient?organization.name=Acme
SELECT r.* FROM Resource r
INNER JOIN ReferenceSearchParam ref ON r.ResourceSurrogateId = ref.ResourceSurrogateId
INNER JOIN Resource org ON ref.ReferenceResourceId = org.ResourceId
INNER JOIN StringSearchParam orgName ON org.ResourceSurrogateId = orgName.ResourceSurrogateId
WHERE r.PartitionId = @currentPartitionId              -- Patients from current partition
  AND org.PartitionId = @currentPartitionId            -- Organizations from current partition
  AND r.ResourceTypeId = @patientResourceTypeId
  AND org.ResourceTypeId = @organizationResourceTypeId
  AND orgName.SearchParamId = @nameSearchParamId
  AND orgName.Text = @searchValue
```

### Functional Behavior by FHIR Operation

#### CRUD Operations
✅ **Fully Compatible** - All operations work within partition scope
- Resources created/accessed within specified or default partition
- Conditional operations search within partition scope only

#### Search Operations
✅ **Partition-Scoped** - All searches automatically limited to partition
- No cross-partition search capabilities
- Resource, system, and compartment searches work within partition

#### History Operations
✅ **Partition-Scoped** - History limited to partition scope

#### Bulk Operations
- **Export**: ✅ Scoped to partition (`/partitions/tenant-a/$export`)
- **Import**: ✅ Creates resources in target partition
- **Reindex**: ✅ Remains server-wide operation (processes all partitions using ResourceSurrogateId ranges)

#### Bundle Operations
✅ **Partition-Scoped** - Transaction/batch bundles operate within single partition
- Cross-partition references will fail with explicit error

#### SearchParameter Management
🔄 **Global Impact** - SearchParameter CRUD affects all partitions uniformly

### Cross-Partition Reference Handling
**Explicit error with clear messaging:**
```csharp
throw new ResourceNotFoundException(
    $"Cross-partition reference not supported. " +
    $"Resource '{resourceType}/{resourceId}' not found in partition '{currentPartition}'. " +
    $"References must be within the same partition."
);
```

### Reindex Strategy
**Server-wide operation with ResourceSurrogateId segmentation:**
- Reindex operations remain server-wide, processing all partitions using existing ResourceSurrogateId range logic
- SearchParameter changes affect all partitions uniformly since definitions are shared
- Single-resource reindex remains partition-scoped
- Benefits: Maintains existing efficient range-based processing, aligns with global SearchParameter model

### Job Processing Model

**Job Storage Schema:**
```sql
CREATE TABLE JobQueue (
    JobId bigint IDENTITY(1,1) NOT NULL,
    RequestingPartitionId smallint NOT NULL,  -- References Partition.PartitionId
    ActualScope varchar(20) NOT NULL,         -- 'Partition' or 'ServerWide'
    JobType varchar(50) NOT NULL,
    Status varchar(20) NOT NULL,
    Definition nvarchar(max) NOT NULL,
    CreatedDate datetimeoffset(7) NOT NULL,
    CONSTRAINT FK_JobQueue_Partition FOREIGN KEY (RequestingPartitionId) REFERENCES Partition(PartitionId)
);
```

**Partition-Scoped Jobs (Export, Import, Bulk Update):**
```csharp
public class ExportJob
{
    public int RequestingPartitionId { get; set; }  // smallint from partition lookup
    public JobScope Scope { get; set; } = JobScope.Partition;

    // Job processes only resources where Resource.PartitionId = RequestingPartitionId
}
```

**Server-Wide Jobs (Reindex):**
```csharp
public class ReindexJob
{
    public int RequestingPartitionId { get; set; }  // For tracking/UI - which partition requested
    public JobScope Scope { get; set; } = JobScope.ServerWide;

    // Job processes ALL partitions using ResourceSurrogateId ranges
    // But job status appears under requesting partition's API: /partitions/{name}/$reindex/{jobId}
}
```

**Job API Examples:**
```
# Tenant A requests export - creates partition-scoped job
POST /partitions/tenant-a/$export
→ JobQueue: RequestingPartitionId=1, ActualScope='Partition'

# Tenant A requests reindex - creates server-wide job but appears tenant-scoped
POST /partitions/tenant-a/$reindex
→ JobQueue: RequestingPartitionId=1, ActualScope='ServerWide'
→ Job status: GET /partitions/tenant-a/$reindex/{jobId}
```

### Partition Management
**Implicit partition creation - no administrative APIs needed:**
- Partitions created automatically on first resource operation
- Partition names validated: 1-64 characters, alphanumeric + `.`, `-`, `_`
- No explicit create/delete/update operations
- List partitions via resource queries if needed

### Resource Surrogate ID Strategy
**Globally unique across partitions:**
- Keeps `ResourceSurrogateId` globally unique
- Simplifies database management and backup/restore
- Search parameter tables join via ResourceSurrogateId only (no PartitionId needed)

## Status
**Proposed**

## Consequences

### Beneficial Effects

#### Storage and Performance Efficiency
- **Storage Optimization**: `smallint` PartitionId (2 bytes) vs `varchar(64)` (128 bytes) - ~98% storage reduction for partition column
- **No Search Parameter Table Overhead**: Search parameter tables don't need PartitionId columns - saves massive storage across billions of search index rows
- **Query Performance**: Partition filtering reduces search space significantly
- **Index Effectiveness**: Search parameter table indexes remain optimal without additional PartitionId columns
- **Parallel Processing**: Bulk operations can process partitions in parallel

#### Logical Data Isolation
- **Clear Tenant Separation**: Resources completely isolated by partition
- **Resource ID Reuse**: Same ResourceId can exist in different partitions (enables data migration scenarios)
- **Reduced Blast Radius**: Operations naturally scoped to smaller data sets
- **Granular Access Control**: Foundation for partition-based authorization

#### Operational Benefits
- **Tenant-Specific Operations**: Backup/restore, export scoped to partitions
- **Feature Flag Control**: Can disable partitioning without code changes
- **SQL Server Only**: No impact on Cosmos DB implementations
- **Backward Compatibility**: Existing deployments continue working unchanged

#### Development and Maintenance
- **Clear Boundaries**: Partition logic isolated to SQL provider
- **Migration Safety**: Existing resources automatically assigned to default partition
- **Testing Isolation**: Partition features tested only against SQL provider

### Adverse Effects

#### Functional Limitations
- **No Cross-Partition Operations**: Major limitation for some multi-tenant scenarios
  - No cross-partition search
  - No cross-partition transactions
  - No cross-partition references
- **SearchParameter-Resource Consistency**: Need to ensure search indexes remain consistent across partitions when SearchParameters change
- **Limited Multi-Partition Reporting**: No built-in cross-partition aggregation capabilities

#### Development Complexity
- **SQL Query Generation**: All queries need partition awareness
  - Every table join requires partition condition
  - WHERE clauses need partition filtering
  - Search parameter queries become more complex
- **Route Parsing**: Optional partition segments increase routing complexity
- **Error Handling**: New error scenarios for cross-partition operations
- **Testing Complexity**: Need to test both partitioned and non-partitioned scenarios

#### Performance Considerations
- **Query Complexity**: Additional WHERE clauses for partition filtering on Resource table
- **Index Efficiency**: Search parameter table indexes remain unchanged and efficient
- **Memory Usage**: Partition lookup table and additional query parameters (minimal impact)

#### Migration and Deployment
- **Schema Migration**: Migration only needed for Resource table and Partition lookup table
- **Index Rebuilding**: Resource table indexes need reconstruction during migration
- **Feature Rollout**: Requires careful coordination of schema and application deployments
- **Backward Compatibility Window**: Mixed behavior during migration period

#### Operational Impact
- **Monitoring Complexity**: Need partition-aware metrics and alerts
- **Troubleshooting**: Issues may be partition-specific
- **Documentation**: Extensive operational guidance required for partition management

### Neutral Effects

#### Preserved Functionality
- **API Compatibility**: No breaking changes to existing FHIR REST API
- **Cosmos DB Unchanged**: No impact on Cosmos DB deployments
- **SearchParameter Behavior**: Global SearchParameter management preserved
- **Performance Parity**: Similar performance for single-partition scenarios

#### Implementation Scope
- **Incremental Rollout**: Can be implemented in phases
- **Feature Toggle**: Can be enabled/disabled per deployment
- **Provider Isolation**: Changes contained within SQL provider

### Risk Mitigation Strategies

#### Schema Migration Complexity
- **Risk**: Direct index rebuilds on billion-row tables could take weeks and fail
- **Mitigation - Hyperscale Strategy**:
  - **Leverage existing PartitionScheme_ResourceTypeId**: Migrate each ResourceType partition independently
  - **Staging table approach**: Create new schema in parallel, avoiding long-running ALTER operations
  - **Partition switching**: Final cutover in milliseconds using `ALTER TABLE...SWITCH PARTITION`
  - **Resumable operations**: Use `RESUMABLE = ON, MAX_DURATION = 240 MINUTES` for index builds
  - **Batch processing**: 10K row batches with progress monitoring and yield points
  - **Rollback per partition**: Each ResourceType can be rolled back independently

#### Query Performance Impact
- **Risk**: Additional WHERE clauses may degrade search performance
- **Mitigation**:
  - Leverage existing AppendHistoryClause pattern for consistent performance
  - Follow existing index naming and partitioning schemes
  - Performance test using existing SqlServerFhirStorageTestsFixture infrastructure
  - Partition filtering reduces search space, likely improving performance

#### JobQueue Column Name Conflict
- **Risk**: Existing JobQueue.PartitionId conflicts with logical partitioning
- **Mitigation**:
  - Use descriptive name `LogicalPartitionId` to avoid confusion
  - Maintain existing physical partitioning for JobQueue performance
  - Clear documentation of dual partitioning concepts

#### Request Context Integration Complexity
- **Risk**: Partition context may not flow correctly through MediatR pipeline
- **Mitigation**:
  - Extend existing FhirRequestContext rather than creating new context
  - Follow established RequestContextAccessor<IFhirRequestContext> pattern
  - Leverage existing FhirRequestContextMiddleware for context injection

#### System Resource Migration Accuracy
- **Risk**: May incorrectly identify system vs user resources during migration
- **Mitigation**:
  - Use existing ResourceType table for authoritative resource type identification
  - Provide verification queries to validate migration results
  - Implement rollback capabilities for partition assignments

#### Cross-Partition Feature Expectations
- **Risk**: Users may expect cross-partition search and transaction capabilities
- **Mitigation**:
  - Clear documentation following existing ADR documentation standards
  - Leverage Azure DICOM precedent for limitation explanations
  - Provide alternative approaches for cross-partition scenarios

#### Testing Strategy Implementation

**Integration Testing Pattern - Follow Existing SqlServerFhirStorageTestsFixture:**
```csharp
[Collection(FhirStorageTestsFixtureCollection.Name)]
public class DataPartitioningIntegrationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
{
    [Fact]
    public async Task GivenPartitionedPatientRequest_WhenSearching_ThenReturnsOnlyPartitionResources()
    {
        // Test partition isolation for regular resources
        // Use existing test infrastructure and patterns
    }

    [Fact]
    public async Task GivenSystemResourceRequest_WhenFromAnyPartition_ThenReturnsSharedResource()
    {
        // Test system resource sharing across partitions
        // Verify SearchParameter, OperationDefinition accessibility
    }

    [Fact]
    public async Task GivenIncludeSearch_WhenCrossingPartitionBoundary_ThenIncludesSystemResources()
    {
        // Test _include operations with system resources
        // Verify Patient?_include=Patient:organization works across partitions
    }

    [Fact]
    public async Task GivenReindexOperation_WhenRequestedFromPartition_ThenProcessesAllPartitions()
    {
        // Test server-wide reindex behavior with partition context tracking
        // Use existing ReindexJobTests patterns
    }
}
```

**Performance Testing Integration:**
```csharp
[Collection(FhirStorageTestsFixtureCollection.Name)]
public class DataPartitioningPerformanceTests : IClassFixture<SqlServerFhirStorageTestsFixture>
{
    [Fact]
    public async Task GivenLargeDataset_WhenPartitionEnabled_ThenSearchPerformanceImproved()
    {
        // Compare search performance with/without partitioning
        // Measure query execution time improvements
    }

    [Fact]
    public async Task GivenMixedSearch_WhenIncludingSystemResources_ThenPerformanceAcceptable()
    {
        // Test search performance when accessing both partition + system resources
        // Validate index effectiveness
    }
}
```

**Migration Testing Strategy:**
```csharp
public class SchemaMigrationTests
{
    [Fact]
    public async Task GivenExistingData_WhenMigrationExecuted_ThenSystemResourcesCorrectlyPartitioned()
    {
        // Verify system resource migration accuracy
        // Test ResourceType table-based identification
    }

    [Fact]
    public async Task GivenLargeDataset_WhenOnlineIndexRebuild_ThenMinimalDowntime()
    {
        // Test online index rebuild performance
        // Measure actual downtime impact
    }
}
```

### Implementation Strategy

#### Database Migration Pattern - Following Existing .diff.sql Approach

**Migration VNext.diff.sql - Infrastructure Setup:**
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

-- Insert system partitions
INSERT INTO dbo.Partition (PartitionName, CreatedDate) VALUES
    ('system', SYSDATETIMEOFFSET()),    -- PartitionId = 1
    ('default', SYSDATETIMEOFFSET());   -- PartitionId = 2

-- Add partition column (non-blocking operation)
ALTER TABLE dbo.Resource ADD PartitionId smallint NOT NULL DEFAULT 2; -- Default partition

-- Update system resources to system partition
UPDATE r SET PartitionId = 1
FROM dbo.Resource r
INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                  'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition');
```

**Migration VNext+1.diff.sql - Hyperscale-Safe Index Strategy:**
```sql
/*************************************************************
    Partition-Level Migration Strategy for Hyperscale Databases
    Leverages existing PartitionScheme_ResourceTypeId for safe migration
**************************************************************/

-- Phase 1: Create staging table with new schema (per partition)
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

    CONSTRAINT PKC_Resource_Staging PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT CH_Resource_Staging_RawResource_Length CHECK (RawResource > 0x0)
) ON PartitionScheme_ResourceTypeId(ResourceTypeId);

-- Phase 2: Create indexes on staging table (faster on empty table)
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_Staging_PartitionId_ResourceTypeId_ResourceId_Version
ON dbo.Resource_Staging (PartitionId, ResourceTypeId, ResourceId, Version)
WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId);

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_Staging_PartitionId_ResourceTypeId_ResourceId
ON dbo.Resource_Staging (PartitionId, ResourceTypeId, ResourceId)
INCLUDE (Version, IsDeleted)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId);

-- Phase 3: Partition-by-partition migration procedure
-- Migrate each ResourceType partition individually (much smaller operations)
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

-- Phase 4: Resumable index operations (SQL Server 2019+)
-- Use resumable operations that can be paused/resumed
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_Final_PartitionId_ResourceTypeId_ResourceId_Version
ON dbo.Resource_Staging (PartitionId, ResourceTypeId, ResourceId, Version)
WITH (
    ONLINE = ON,
    RESUMABLE = ON,
    MAX_DURATION = 240 MINUTES,  -- 4 hour chunks
    MAXDOP = 4,
    DATA_COMPRESSION = PAGE
) ON PartitionScheme_ResourceTypeId(ResourceTypeId);
```

**Migration Execution Strategy - Partition Switching:**
```sql
-- Phase 5: Partition switching (minimal downtime)
-- Switch each partition individually during maintenance windows

-- Example for switching ResourceTypeId = 1 partition
BEGIN TRANSACTION;

    -- Verify data integrity
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

**JobQueue Integration (Avoid Column Name Conflict):**
```sql
-- Add logical partition tracking to existing JobQueue
ALTER TABLE dbo.JobQueue ADD LogicalPartitionId smallint NULL;
ALTER TABLE dbo.JobQueue ADD CONSTRAINT FK_JobQueue_LogicalPartition
    FOREIGN KEY (LogicalPartitionId) REFERENCES Partition(PartitionId);

-- Migrate existing jobs to 'default' partition (first user partition)
-- This ensures existing jobs continue operating in the default user data space
UPDATE dbo.JobQueue SET LogicalPartitionId = 2 WHERE LogicalPartitionId IS NULL;
ALTER TABLE dbo.JobQueue ALTER COLUMN LogicalPartitionId smallint NOT NULL;

-- Note: Existing jobs remain in default partition and continue normal operation
-- No job data or behavior changes - only adds partition tracking for new jobs
```

#### Application Code Integration - Extend Existing Patterns

**Request Context Enhancement:**
```csharp
// Extend existing FhirRequestContext (no new class needed)
namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public partial class FhirRequestContext : IFhirRequestContext
    {
        public int? LogicalPartitionId { get; set; }
        public string PartitionName { get; set; }
        public int SystemPartitionId => 1; // Always 1 for 'system' partition

        public int GetEffectivePartitionId(string resourceType = null)
        {
            if (SystemResourceTypes.Types.Contains(resourceType))
                return SystemPartitionId;
            return LogicalPartitionId ?? 2; // Default partition
        }
    }
}
```

**Middleware Registration:**
```csharp
// Register middleware in startup pipeline before routing
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // ... existing middleware

    app.UseMiddleware<PartitionRoutingMiddleware>();  // Before routing
    app.UseRouting();

    // ... existing middleware
}
```

**SQL Query Generation - Follow AppendHistoryClause Pattern:**
```csharp
// Add to SqlQueryGenerator following existing WHERE clause pattern
private void AppendPartitionClause(
    in IndentedStringBuilder.DelimitedScope delimited,
    string resourceType = null,
    string tableAlias = null)
{
    if (!_partitioningEnabled) return;

    var context = _fhirRequestContextAccessor.RequestContext;
    if (context?.LogicalPartitionId == null) return;

    delimited.BeginDelimitedElement();

    if (!string.IsNullOrEmpty(resourceType) && SystemResourceTypes.Types.Contains(resourceType))
    {
        StringBuilder.Append(VLatest.Resource.PartitionId, tableAlias)
                    .Append(" = ").Append(context.SystemPartitionId);
    }
    else
    {
        StringBuilder.Append(VLatest.Resource.PartitionId, tableAlias)
                    .Append(" = ").Append(context.LogicalPartitionId.Value);
    }
}

// Integration point in existing methods
private void HandleTableKindNormal(SearchParamTableExpression expression, SearchOptions context)
{
    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
    {
        AppendHistoryClause(delimited, context.ResourceVersionTypes, expression);
        AppendPartitionClause(delimited, expression.ResourceType, tableAlias); // Add this line
        // ... existing code continues
    }
}
```

**Configuration Extension:**
```csharp
// Extend existing SqlServerDataStoreConfiguration
public class SqlServerDataStoreConfiguration
{
    // Existing properties unchanged...

    public DataPartitioningConfiguration DataPartitioning { get; set; } = new();
}

public class DataPartitioningConfiguration
{
    public bool Enabled { get; set; } = false;
    public string DefaultPartitionName { get; set; } = "default";
    public string SystemPartitionName { get; set; } = "system";
}
```

**Reindex Operation Enhancement - Extend Existing ReindexOrchestratorJob:**
```csharp
// Enhance existing ReindexOrchestratorJob with partition context tracking
public class ReindexOrchestratorJob : IJob
{
    public override async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
    {
        // Extract requesting partition from job data (for tracking/UI purposes)
        var requestingPartition = jobInfo.Data?.ToString() ?? "unknown";

        progress.Report($"Starting server-wide reindex requested by partition: {requestingPartition}");

        // Continue with existing server-wide ResourceSurrogateId range processing
        // All existing reindex logic remains unchanged - still processes all partitions

        return await base.ExecuteAsync(jobInfo, progress, cancellationToken);
    }
}

// Job creation includes partition context
public class CreateReindexRequestHandler : IRequestHandler<CreateReindexRequest, CreateReindexResponse>
{
    public async Task<CreateReindexResponse> Handle(CreateReindexRequest request, CancellationToken cancellationToken)
    {
        var requestingPartition = _fhirRequestContextAccessor.RequestContext.PartitionName ?? "default";

        var jobInfo = new JobInfo
        {
            // Store requesting partition for job tracking/status UI
            Data = requestingPartition,
            LogicalPartitionId = _fhirRequestContextAccessor.RequestContext.LogicalPartitionId,
            // ... existing job creation logic unchanged
        };

        // Job execution remains server-wide but tracks requesting partition
        return await _jobService.EnqueueJobAsync(jobInfo, cancellationToken);
    }
}
```

**System Resource Type Identification - Leverage Existing Infrastructure:**
```csharp
// Hardcoded system resource types following existing patterns
public static class SystemResourceTypes
{
    public static readonly HashSet<string> Types = new()
    {
        "SearchParameter",
        "OperationDefinition",
        "StructureDefinition",
        "ValueSet",
        "CodeSystem",
        "CapabilityStatement",
        "CompartmentDefinition"
    };

    public static readonly string[] TypeArray = Types.ToArray();
}

// Migration validation query
public static class PartitionMigrationQueries
{
    public const string ValidateSystemResourceMigration = @"
        SELECT
            rt.Name as ResourceType,
            r.PartitionId,
            COUNT(*) as ResourceCount,
            CASE
                WHEN r.PartitionId = 1 THEN 'System Partition'
                WHEN r.PartitionId = 2 THEN 'Default Partition'
                ELSE 'Other Partition'
            END as PartitionType
        FROM dbo.Resource r
        INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
        WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                          'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
        GROUP BY rt.Name, r.PartitionId
        ORDER BY rt.Name, r.PartitionId;";

    public const string ValidatePartitionIntegrity = @"
        -- Verify all system resources are in system partition
        SELECT COUNT(*) as MisplacedSystemResources
        FROM dbo.Resource r
        INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
        WHERE rt.Name IN ('SearchParameter', 'OperationDefinition', 'StructureDefinition',
                          'ValueSet', 'CodeSystem', 'CapabilityStatement', 'CompartmentDefinition')
          AND r.PartitionId != 1;"; // Should return 0
}
```

#### Implementation Phases

#### Phase 1: Foundation
**Goal:** Deploy partition-aware application code with feature disabled
- Extend existing `FhirRequestContext` with partition properties
- Implement `PartitionRoutingMiddleware` for URL rewriting and context injection
- Implement `AppendPartitionClause` in `SqlQueryGenerator`
- Extend `SqlServerDataStoreConfiguration` with partitioning options
- **Deliverable:** Partition-aware code deployed, feature flag OFF, no schema changes

#### Phase 2: Migration (Hyperscale-Safe Approach)
**Goal:** Execute partition-by-partition migration for hyperscale databases
- **Week 1**: Deploy migration VNext.diff.sql (Partition table, staging table creation)
- **Weeks 2-8**: Execute partition-by-partition migration:
  - Start with smallest ResourceType partitions (e.g., CompartmentDefinition, StructureDefinition)
  - Use `MigrateResourcePartition` procedure for each ResourceTypeId
  - 2-3 partitions per week during maintenance windows
  - Validate data integrity after each partition
- **Week 9-10**: Create resumable indexes on staging table
- **Week 11**: Partition switching cutover (minimal downtime per ResourceType)
- **Deliverable:** Database schema migrated with minimal risk and downtime

#### Phase 3: Activation
**Goal:** Enable partitioning APIs following existing route patterns
- Enable DataPartitioning.Enabled configuration flag
- Activate `/partitions/{name}/...` route templates in controllers
- Implement implicit partition creation in partition service
- Validate complete FHIR API functionality per partition
- **Deliverable:** Multi-tenant partition APIs operational

#### Phase 4: Production Hardening
**Goal:** Leverage existing operational patterns for partition support
- Extend existing monitoring infrastructure for partition metrics
- Update existing backup/restore procedures for partition awareness
- Performance tuning using existing SqlQueryGenerator optimization patterns
- Integration testing following SqlServerFhirStorageTestsFixture patterns
- **Deliverable:** Production-ready partitioning integrated with existing operations

### Configuration Examples by Phase

#### Phase 1 Configuration (Foundation)
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": false
      }
    }
  }
}
```
**Application State:** New servers always create 'system' and 'default' partitions, partition-aware filtering always active
**Database State:** System resources in 'system' partition (PartitionId=1), user resources in 'default' partition (PartitionId=2)

#### Phase 2 Configuration (Migration - Existing Servers Only)
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": false
      }
    }
  }
}
```
**Database State:** Existing server schema migrated, system resources moved to 'system' partition, user resources to 'default' partition
**Application State:** Partition-aware filtering active, ready for API activation

#### Phase 3 Configuration (Activation)
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": true
      }
    }
  }
}
```
**System State:** Partitioning API endpoints available, implicit partition creation enabled
**API State:** `/partitions/{name}/...` endpoints operational, backward compatibility maintained through 'default' partition

#### Phase 4 Configuration (Production)
```json
{
  "FhirServer": {
    "Features": {
      "DataPartitions": {
        "Enabled": true
      }
    }
  }
}
```
**Production State:** Full operational capabilities, monitoring via existing metrics infrastructure

### System Resource Sharing and Routing Architecture

**Middleware Integration with Request Context:**
```csharp
public class PartitionRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        string partitionName = null;

        if (path.StartsWithSegments("/partitions"))
        {
            // Extract partition from URL: /partitions/{name}/...
            var segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                partitionName = segments[1]; // partitionName from URL

                // Rewrite URL by removing /partitions/{name} prefix
                context.Request.Path = "/" + string.Join("/", segments.Skip(2));

                // Validate partition name format (64 chars, alphanumeric + . - _)
                if (!IsValidPartitionName(partitionName))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid partition name format");
                    return;
                }
            }
        }
        else
        {
            // Non-partitioned requests → 'default' partition
            partitionName = "default";
        }

        // Store partition context in existing FhirRequestContext
        var fhirContext = _fhirRequestContextAccessor.RequestContext;
        fhirContext.PartitionName = partitionName;
        fhirContext.LogicalPartitionId = await GetPartitionId(partitionName);

        await _next(context);
    }

    private static bool IsValidPartitionName(string name)
    {
        return !string.IsNullOrEmpty(name) &&
               name.Length <= 64 &&
               name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
    }
}
```

**Key Benefits of Middleware Approach:**
- **No Controller Changes**: All existing MVC routes and controllers work unchanged
- **Clean Separation**: Partition logic isolated from business logic
- **Leverages Existing Infrastructure**: Uses existing `RequestContextAccessor<IFhirRequestContext>` pattern
- **Transparent Operation**: Downstream code sees normal FHIR URLs, partition context available in request context
- **Error Handling**: Centralized validation and error handling for partition names

**Database Setup Strategy (All Servers):**
```sql
-- Every server gets these partitions from day 1
INSERT INTO dbo.Partition (PartitionName) VALUES
    ('system'),   -- PartitionId = 1, for system resources
    ('default');  -- PartitionId = 2, for default user resources

-- System resources always go to 'system' partition
-- User resources go to 'default' partition (or other tenant partitions when created)
ALTER TABLE dbo.Resource ADD PartitionId smallint NOT NULL DEFAULT 2; -- Default to user partition

-- For new installations, system resources created in 'system' partition during initial setup
-- For existing installations, migration moves system resources to 'system' partition
```

**Resource Handler Architecture:**
```csharp
public class FhirResourceHandler
{
    public async Task<IActionResult> HandleResourceRequest(
        string partitionName,
        string resourceType,
        string resourceId = null)
    {
        if (SystemResourceTypes.Types.Contains(resourceType))
        {
            // System resources: Get from 'system' partition but present as tenant-scoped
            var systemResource = await GetSystemResource(resourceType, resourceId);
            if (systemResource != null)
            {
                // Modify resource URLs to appear partition-scoped
                ModifyResourceUrlsForPartition(systemResource, partitionName);
                return Ok(systemResource);
            }
            return NotFound();
        }
        else
        {
            // Regular resources: Truly partition-scoped
            var partitionId = await GetPartitionId(partitionName);
            var resource = await GetPartitionedResource(partitionId, resourceType, resourceId);
            return resource != null ? Ok(resource) : NotFound();
        }
    }

    private void ModifyResourceUrlsForPartition(Resource resource, string partitionName)
    {
        // Ensure all self-links and references appear under the partition URL space
        // e.g., change /SearchParameter/123 to /partitions/{name}/SearchParameter/123
        if (resource.Id != null)
        {
            resource.Id = $"/partitions/{partitionName}/{resource.ResourceType}/{resource.Id}";
        }
        // Modify other URLs in the resource as needed
    }
}
```

**Benefits of This Architecture:**
- **Efficient Storage**: System resources stored once in 'system' partition, accessible from any partition context
- **Perfect Isolation**: Tenants can only access their own user resources, but all can access shared system resources
- **API Consistency**: Every partition has identical FHIR API surface including system resources
- **Search Operations Work Naturally**: _include and chained searches can access both partition resources and system resources
- **Clean Security**: System resources globally accessible, user resources partition-isolated

**Implementation Notes:**
- System resources physically stored in 'system' partition (PartitionId = 1)
- Resource URLs modified on-the-fly to appear partition-scoped when served
- Search queries can access both current partition and 'system' partition resources
- _include operations and chained searches work naturally across partition boundaries for system resources
- Search indexer remains singleton, processes all partitions including 'system'
- Job processing inherits partition context from requesting partition
- **Existing Job Migration**: All pre-existing jobs automatically assigned to 'default' partition and continue normal operation without changes

### Monitoring and Observability

#### Key Metrics
- **Partition Distribution**: Resource count per partition
- **Query Performance**: Average query time by partition
- **Cross-Partition Errors**: Frequency of cross-partition reference attempts
- **Reindex Coordination**: Success rate of multi-partition operations

#### Alerting Scenarios
- Partition growth imbalances
- Cross-partition error rate spikes
- Query performance degradation
- Failed partition operations

## References
- [Azure DICOM Data Partitions Documentation](https://learn.microsoft.com/en-us/azure/healthcare-apis/dicom/data-partitions)
- [FHIR R4 Specification](https://hl7.org/fhir/R4/)
- [SQL Server Partitioning Best Practices](https://docs.microsoft.com/en-us/sql/relational-databases/partitions/partitioned-tables-and-indexes)
- Related ADRs:
  - ADR 2512: SearchParameter Concurrency Management (similar SQL provider isolation patterns)
  - ADR 2505: Eventual Consistency for Search Param Indexes (related to SearchParameter management)
  - ADR 2504: Limit Concurrent Merge Resources (similar performance considerations)