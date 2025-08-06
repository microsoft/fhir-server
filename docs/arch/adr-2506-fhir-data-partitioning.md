# ADR 2506: FHIR Data Partitioning Support

Labels: [API](https://github.com/microsoft/fhir-server/labels/Area-API), [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL), [CosmosDB](https://github.com/microsoft/fhir-server/labels/Area-CosmosDB)

## Context

Healthcare organizations often require logical data isolation within a single FHIR server instance to support multi-tenancy scenarios. Examples include:
- Healthcare systems managing data for multiple hospitals or clinics
- Research organizations maintaining separate datasets for different studies  
- Managed service providers serving multiple healthcare organizations
- Organizations requiring strict data segregation for compliance or privacy reasons

Currently, the Microsoft FHIR Server does not support data partitioning, requiring separate server instances for data isolation. This approach has limitations in terms of cost, management overhead, and resource utilization.

The Azure DICOM Service successfully implements data partitioning using a `workspaces/{workspaceId}/` URL prefix pattern, providing a proven model for healthcare data partitioning in Microsoft's healthcare APIs.

## Decision

We will implement data partitioning support in the Microsoft FHIR Server using a partition-based URL routing scheme similar to the Azure DICOM Service pattern.

### API Routing Changes

#### Partitioned APIs
The following APIs will be moved under a `partition/{partitionId}/` URL prefix:

**Resource Operations:**
- `GET|POST /partition/{partitionId}/Patient` (and all resource types)
- `GET|PUT|PATCH|DELETE /partition/{partitionId}/Patient/{id}`
- `GET /partition/{partitionId}/Patient/{id}/_history`
- `GET /partition/{partitionId}/Patient/{id}/_history/{vid}`
- `POST /partition/{partitionId}/Patient/_search`

**Bundle Operations:**
- `POST /partition/{partitionId}/` (Bundle transactions and batches)
- All Bundle entries must reference resources within the same partition
- Cross-partition references in Bundles will be rejected

**Search and Compartments:**
- `GET /partition/{partitionId}/Patient/_search`
- `GET /partition/{partitionId}/Patient/{id}/Observation` (compartment searches)
- `POST /partition/{partitionId}/Patient/_search`
- All search operations are scoped to the specified partition
- Compartment definitions and assignments respect partition boundaries

**Resource-Specific Operations:**
- `POST /partition/{partitionId}/Patient/{id}/$validate`
- `GET /partition/{partitionId}/Patient/{id}/$everything`
- `DELETE /partition/{partitionId}/Patient/{id}/$purge-history`

**Bulk Operations:**
- `POST /partition/{partitionId}/Patient/$bulk-delete`
- `POST /partition/{partitionId}/Patient/$bulk-update`
- `GET /partition/{partitionId}/Patient/$includes`

**Export Operations:**
- `GET /partition/{partitionId}/$export`
- `GET /partition/{partitionId}/Patient/$export`
- `GET /partition/{partitionId}/Group/{id}/$export`

**Import Operations:**
- `POST /partition/{partitionId}/$import`

**Reindex Operations:**
- `POST /partition/{partitionId}/$reindex`
- `POST /partition/{partitionId}/Patient/{id}/$reindex`

#### Global APIs (Remain Unpartitioned)
The following APIs will remain at the root level as they provide server-wide functionality:

**Server Metadata:**
- `GET /metadata` - CapabilityStatement describing server capabilities
- `GET /.well-known/smart-configuration` - SMART on FHIR configuration
- `GET /health/check` - Health check endpoint

**Administrative Operations:**
- `GET|POST /SearchParameter/$status` - Search parameter management
- `GET /_operations/export/{jobId}` - Job status endpoints (cross-partition visibility for admin)
- `GET /_operations/import/{jobId}`
- `GET /_operations/reindex/{jobId}`
- `GET /_operations/bulk-delete/{jobId}`
- `GET /_operations/bulk-update/{jobId}`

**Version Information:**
- `GET /$versions` - Server version information
- `GET /OperationDefinition/{operationName}` - Operation definitions

#### APIs that might no longer work on root
Root-level APIs that operate on resources will be deprecated in favor of partition-scoped equivalents:
- `GET|POST /Patient` → `GET|POST /partition/{partitionId}/Patient`
- `POST /` (Bundle operations) → `POST /partition/{partitionId}/`
- Resource searches and operations → Partition-scoped equivalents

#### Backward Compatibility
Root-level resource APIs will continue to work but will be deprecated:
- Root APIs will operate against a "default" partition for existing deployments
- New deployments will require explicit partition specification
- A configuration option will control whether root APIs are enabled
- Deprecation warnings will be included in API responses

#### Cross-Partition Considerations
- References between resources in different partitions will be handled as external references
- Bundle operations cannot span multiple partitions
- Search operations are scoped to a single partition
- Compartment searches respect partition boundaries

### Implementation Details

#### Route Constraint Updates
The existing route constraints in `KnownRoutes.cs` will be extended:
```csharp
// New partition-aware routes
public const string PartitionPrefix = "partition/{partitionId}";
public const string PartitionedResourceType = PartitionPrefix + "/" + ResourceTypeRouteSegment;
public const string PartitionedResourceTypeById = PartitionedResourceType + "/" + IdRouteSegment;
// ... other partitioned route patterns
```

#### Controller Updates
Controllers will be updated to handle partition context:
- `FhirController` will support both root and partitioned routes
- New route attributes will include partition parameters
- Partition validation will occur in action filters
- Request context will include partition information

#### Middleware Integration
New middleware will be added to:
- Extract partition information from routes
- Validate partition access permissions
- Set partition context for downstream components
- Handle partition-specific configuration

### Database Structural Changes

#### SQL Server Changes
Add partition support across all data storage tables:

**Core Tables:**
```sql
-- Add PartitionId column to Resource table
ALTER TABLE dbo.Resource 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

-- Add PartitionId to all search parameter tables
ALTER TABLE dbo.StringSearchParam 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

ALTER TABLE dbo.TokenSearchParam 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

ALTER TABLE dbo.ReferenceSearchParam 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

ALTER TABLE dbo.CompartmentAssignment 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

-- (Similar for all other search param and compartment tables)
```

**Index Updates:**
- All existing indexes will be rebuilt to include PartitionId as the first column
- New partition-aware stored procedures will be created
- Existing stored procedures will be updated to filter by PartitionId

**Job Management:**
```sql
-- Add PartitionId to job tracking tables
ALTER TABLE dbo.TaskInfo 
ADD PartitionId varchar(64) NULL; -- Nullable for global jobs

ALTER TABLE dbo.ReindexJob 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';

-- Update TaskInfo for partition-aware job management
ALTER TABLE dbo.TaskInfo 
ADD PartitionId varchar(64) NULL; -- Nullable for global jobs

-- Transaction tracking needs partition awareness
ALTER TABLE dbo.Transactions 
ADD PartitionId varchar(64) NOT NULL DEFAULT 'default';
```

#### Cosmos DB Changes
- Add `partitionId` property to all resource documents
- Update partition key strategy to include partition information
- Modify queries to include partition filtering
- Update stored procedures and triggers

#### Schema Migration Strategy
1. Add PartitionId columns with default values to maintain existing data
2. Create new schema version with partition-aware objects
3. Migrate existing data to use "default" partition identifier
4. Update application code to use new partition-aware data access patterns

### Async Jobs and Partitioning

#### Export Operations
- Export jobs will be scoped to specific partitions
- Job status and results will be partition-isolated
- Cross-partition exports will require administrative privileges
- Export job URLs: `/partition/{partitionId}/_operations/export/{jobId}`

#### Import Operations  
- Import jobs will target specific partitions
- Imported resources will be assigned to the target partition
- Import validation will respect partition boundaries
- Import job URLs: `/partition/{partitionId}/_operations/import/{jobId}`

#### Reindex Operations
- Reindex jobs will be partition-scoped by default
- Global reindex operations will be administrative functions
- Partition-specific reindex jobs ensure resource consistency within partition boundaries
- Reindex job URLs: `/partition/{partitionId}/_operations/reindex/{jobId}`

#### Job Queue Management
- Job queues will maintain partition context
- Resource locking and coordination will respect partition boundaries
- Job priority and throttling will be partition-aware
- Cross-partition job coordination will be handled by administrative services

### Security and Authorization

#### Partition Access Control
- Authentication tokens will include partition scope claims
- Authorization policies will enforce partition-level access
- Users will be granted access to specific partitions
- Administrative users may have cross-partition access

#### Data Isolation
- Partition boundaries will be enforced at the data access layer
- Cross-partition queries will be explicitly prevented (except for admin operations)
- Audit logs will include partition context
- Backup and restore operations will support partition-level granularity

### Configuration and Management

#### Partition Configuration
```json
{
  "FhirServer": {
    "Partitioning": {
      "Enabled": true,
      "DefaultPartition": "default",
      "AllowRootApi": true,
      "RequirePartitionHeader": false,
      "MaxPartitions": 1000
    }
  }
}
```

#### Partition Management APIs
```
POST /admin/partitions - Create new partition
GET /admin/partitions - List partitions  
DELETE /admin/partitions/{partitionId} - Delete partition (with safety checks)
PUT /admin/partitions/{partitionId}/status - Enable/disable partition
```

## Status

Proposed

## Consequences

### Benefits

**Multi-Tenancy Support:**
- Enables true multi-tenant FHIR deployments
- Provides logical data isolation without separate server instances
- Reduces infrastructure costs and management overhead

**Scalability Improvements:**
- Better resource utilization across multiple organizations
- Partition-scoped operations improve performance
- Enables horizontal scaling strategies

**Compliance and Security:**
- Stronger data isolation for regulatory compliance
- Partition-level access controls enhance security
- Audit trails with partition context improve governance

**Operational Efficiency:**
- Centralized server management for multiple tenants
- Partition-scoped backup and restore capabilities
- Simplified maintenance and updates

### Challenges and Risks

**Breaking Changes:**
- URL structure changes require client application updates
- Existing integrations need modification for explicit partition support
- Legacy systems may require adaptation period

**Implementation Complexity:**
- Significant database schema changes required
- Complex migration path from unpartitioned to partitioned deployments
- Cross-partition operations need careful design

**Performance Considerations:**
- Additional PartitionId filtering in all queries
- Index overhead for partition columns
- Potential query plan changes requiring optimization

**Operational Overhead:**
- Partition management and monitoring requirements
- More complex backup and disaster recovery procedures
- Additional security configuration and access control management

### Migration Strategy

**Phase 1: Foundation**
- Implement partition-aware data access layer
- Add partition columns to database schemas
- Create partition management APIs

**Phase 2: API Updates**
- Implement partitioned routing
- Update controllers and request handling
- Add partition context to request pipeline

**Phase 3: Async Operations**
- Update job management for partition awareness
- Implement partition-scoped async operations
- Add cross-partition administrative functions

**Phase 4: Migration Tools**
- Create tools for migrating existing deployments
- Implement data migration utilities
- Provide rollback capabilities

### Future Considerations

**Cross-Partition Operations:**
- Research use cases requiring cross-partition queries
- Design secure cross-partition reference mechanisms
- Consider federation patterns for multi-partition searches

**Performance Optimization:**
- Implement partition-aware caching strategies
- Optimize database queries for partition filtering
- Consider partition-specific scaling policies

**Advanced Features:**
- Partition-level feature flags and configuration
- Dynamic partition creation and management
- Automated partition lifecycle management