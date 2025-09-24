# ADR 2509: Data Partitions for Multi-Tenant FHIR Server
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL), [Architecture](https://github.com/microsoft/fhir-server/labels/Area-Architecture)

## Context

Healthcare organizations often require logical data separation within a single FHIR server instance for various use cases:
- Multi-tenant scenarios where different organizations or departments need isolated data
- Data segregation for different projects, studies, or regulatory domains
- Compliance requirements that mandate data boundaries (e.g., HIPAA, GDPR)
- Simplified data management and governance

Currently, the FHIR server treats all data as belonging to a single global namespace. While this works for single-tenant scenarios, it creates challenges for organizations needing logical data separation without deploying multiple FHIR server instances.

The Azure DICOM service has successfully implemented data partitions with the following characteristics:
- Partition names are up to 64 alphanumeric characters with `.`, `-`, and `_` allowed
- Partitions are created implicitly when first accessed via API
- Default partition is `Microsoft.Default` for existing data
- Cross-partition queries are not supported
- Feature cannot be disabled once enabled

### Current SQL Architecture Analysis

The current SQL schema uses partitioned tables based on `ResourceTypeId`:
- Resource table: `ResourceTypeId, ResourceId, Version, ResourceSurrogateId` as primary key
- Search parameter tables: All partitioned by `ResourceTypeId`
- Jobs and operations: Currently operate at server level
- System data: Currently stored globally without partition context

### Job Processing Considerations

Current bulk operations (export, import, reindex, bulk update) operate at different scopes:
- Export: System-level or resource type-level operations
- Import: System-level operations with resource type processing
- Reindex: System-level operations with resource type and search parameter scoping
- Bulk Update: Resource type-level operations with search parameter filtering

## Decision

We will implement a data partitioning capability for the FHIR server with the following design:

### 1. Database Schema Changes

**Add Partition Column Strategy:**

*Option A: Partition Column Only in Resource Table (Recommended)*
- Add `PartitionName varchar(64)` column only to `Resource` table
- Search parameter queries already JOIN to Resource table for final result set
- **Benefits**: Simpler schema changes, minimal storage overhead, no performance impact
- **Drawbacks**: None significant - existing query patterns already join to Resource table

*Option B: Partition Column in All Tables*
- Add `PartitionName varchar(64)` column to all resource-related tables:
  - `Resource` table (primary resource storage)
  - All search parameter tables (`TokenSearchParam`, `StringSearchParam`, etc.)
  - `ResourceChangeData` and related tables
  - `CompartmentAssignment` table
- **Benefits**: Direct partition filtering in search parameter tables
- **Drawbacks**: More schema changes, increased storage overhead, unnecessary complexity

**Recommendation**: Option A (partition only in Resource table) since all search queries already join to the Resource table to build the final result set. This approach provides the same logical isolation with significantly simpler implementation.

**Uniqueness Constraint Updates:**
- Update primary uniqueness constraint to include partition:
  ```sql
  CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_PartitionName ON dbo.Resource
  (ResourceTypeId, ResourceId, PartitionName)
  ```
- This allows same ResourceId across different partitions (e.g., `Patient/123` in multiple tenants)
- Maintains uniqueness within each partition

**System-Level Tables Remain Unpartitioned:**
- Job management tables (`TaskInfo`, `JobQueue`)
- Schema management tables (`Parameters`, `SchemaMigrationProgress`)
- System configuration tables (`System`, `SearchParam`, `ResourceType`)

**Partition Schema Version:**
- Increment schema version to V96
- Create migration script `96.diff.sql` to add partition columns
- Add `NOT NULL` constraint with default value `'default'` for existing data
- Update indexes to include `PartitionName` where appropriate

### 2. API Surface Changes

**Partition-Scoped Endpoints:**
All FHIR CRUD, search, and bulk operations will support partition-scoped URIs:
```
/partitions/{partitionName}/Patient
/partitions/{partitionName}/Patient/123
/partitions/{partitionName}/Patient?name=John
/partitions/{partitionName}/$export
```

**System-Level Endpoints Remain Unchanged:**
- Capability statement: `/metadata`
- Operation definitions: `/$export-operation-status`
- Health checks and configuration endpoints

**Partition Management:**
- `GET /partitions` - List all partitions
- Implicit partition creation during first resource operation
- No explicit partition creation, update, or deletion APIs

### 3. Data Isolation Strategy

**Logical Separation:**
- All resource data strictly isolated by partition
- Cross-partition queries explicitly not supported
- Search operations scoped to single partition
- History and versioning isolated within partition

**Resource ID Uniqueness:**
- Resource IDs are unique within each partition (not globally unique)
- Same Resource ID can exist in multiple partitions (e.g., `Patient/123` in different tenants)
- Partition name becomes part of the logical identity of a resource
- Full resource reference: `{base}/partitions/{partitionName}/{resourceType}/{id}`

**Default Partition:**
- Name: `'default'` (aligned with current data)
- All existing data migrated to default partition
- Backward compatibility for non-partitioned API calls

### 4. Job and Operation Partitioning Strategy

**Partition-Scoped Jobs:**
- Export operations: Scoped to specific partition
- Import operations: Import to specific partition
- Bulk Update operations: Scoped to specific partition
- Reindex operations: Scoped to specific partition

**Server-Level Jobs:**
- Schema migration jobs: Remain server-level
- System maintenance jobs: Remain server-level
- Cross-partition administrative operations: Server-level

**Job Processing Model:**
- Job definitions include `PartitionName` parameter
- Worker jobs inherit partition context from orchestrator
- Job status and results isolated by partition where applicable
- Administrative jobs (reindex across all partitions) run at server level

### 5. Configuration and Provisioning

**Feature Flag:**
- `FhirServer:Features:DataPartitions:Enabled` (default: false)
- Can only be enabled during initial deployment or on empty database
- Cannot be disabled once enabled with data in multiple partitions

**Partition Naming:**
- 1-64 characters: alphanumeric, `.`, `-`, `_`
- Case-sensitive
- Reserved names: `system`, `admin`, `metadata`

### 6. Backward Compatibility

**Non-Partitioned API Calls:**
- Continue to work against `'default'` partition
- Existing applications work without modification
- Capability statement indicates partition support via extension

**Data Migration:**
- Existing data automatically assigned to `'default'` partition
- No data loss or service interruption during migration

### 7. Implementation Phases

**Phase 1: Core Infrastructure**
- Schema migration with partition column
- Basic partition-scoped CRUD operations
- Partition management APIs

**Phase 2: Search and Query**
- Partition-scoped search operations
- Search parameter index updates
- Compartment support within partitions

**Phase 3: Bulk Operations**
- Partition-scoped export operations
- Partition-scoped import operations
- Partition-scoped bulk update operations

**Phase 4: Advanced Features**
- Partition-scoped reindex operations
- Administrative cross-partition operations
- Monitoring and metrics per partition

## Status

Proposed

## Consequences

### Benefits:
- **Logical Data Isolation**: Clear separation of data for multi-tenant scenarios
- **Compliance Support**: Helps meet regulatory requirements for data segregation
- **Simplified Administration**: Easier data management and governance per partition
- **Scalability**: Better performance through reduced data scope for operations
- **Consistency**: Aligns with Azure DICOM service patterns for familiar experience

### Challenges:
- **Schema Complexity**: Minimal - only Resource table needs partition column modification
- **Index Management**: Simple - update Resource table indexes to include partition information
- **Query Performance**: No impact - queries already join to Resource table for results
- **Job Complexity**: Bulk operations become more complex with partition awareness
- **Migration Effort**: Reduced complexity with single-table approach for existing deployments

### Technical Impacts:
- **Database Size**: Minimal increase due to partition column only in Resource table
- **Query Patterns**: All resource queries must include partition context (already join to Resource table)
- **Index Strategy**: Simple approach - partition column only in Resource table leverages existing query patterns
- **Resource Identity**: Resource IDs become partition-scoped rather than globally unique
- **Backup/Restore**: Need partition-aware backup and restore strategies
- **Monitoring**: Metrics and logging need partition context
- **Testing**: Comprehensive testing across single and multi-partition scenarios, including Resource ID collision scenarios

### Operational Impacts:
- **Deployment**: One-time schema migration required
- **Configuration**: New configuration parameters for partition enablement
- **Documentation**: Extensive documentation updates for partition APIs
- **Training**: Team training on partition concepts and operations

### Risk Mitigation:
- **Feature Flag**: Safe rollout with configuration-controlled enablement
- **Default Partition**: Ensures backward compatibility for existing applications
- **Schema Versioning**: Leverages existing migration infrastructure
- **Incremental Rollout**: Phased implementation reduces risk

### Future Considerations:
- **Cross-Partition Analytics**: May need future capability for reporting across partitions
- **Partition Migration**: Tools for moving data between partitions (considering Resource ID conflicts)
- **Advanced Security**: Partition-level access controls and authentication
- **Performance Optimization**: Partition-specific tuning and optimization strategies
- **Resource ID Management**: Tooling to detect and manage Resource ID conflicts during partition operations
- **Search Parameter Table Partitioning**: Future option to add partition columns to search parameter tables if direct filtering becomes needed (unlikely given current query patterns)