# ADR 2512: SearchParameter Concurrency Management - Application Level Locking and Database Optimistic Concurrency
*Labels*: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL) | [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [SearchParameter](https://github.com/microsoft/fhir-server/labels/Area-SearchParameter)

---

## Context

### Problem Statement
The FHIR server was experiencing race conditions when multiple concurrent requests attempted to create, update, or delete the same SearchParameter resource simultaneously. This core race condition manifested in failed SearchParameter operations with error messages such as:

```json
{
  "resourceType": "OperationOutcome",
  "issue": [
    {
      "severity": "error",
      "code": "not-supported",
      "diagnostics": "https://nshealth.ca/fhir/SearchParameter/HealthcareServiceNotAvailableStart"
    },
    {
      "severity": "error", 
      "code": "exception",
      "diagnostics": "An error occurred updating the custom search parameter. The issue must be resolved and the update resubmitted to be applied."
    }
  ]
}
```

### Root Cause Analysis
The race condition occurred because SearchParameter operations involve multiple coordinated updates that were not properly synchronized:
- **SearchParam Status Table**: Tracking parameter status (Enabled, Disabled, PendingDelete, etc.)
- **In-Memory SearchParameterDefinitionManager**: Caching parameter definitions for fast access
- **Resource Table**: Storing the actual FHIR SearchParameter resource
- **Search Indexes**: Maintaining search parameter metadata for query optimization

When multiple requests operated on the same SearchParameter URI concurrently, these components could become inconsistent, leading to operational failures. The lack of proper concurrency control allowed simultaneous modifications to interfere with each other, resulting in unpredictable behavior and failed operations.

### Architecture Context
The FHIR server operates in distributed environments with:
- **Multiple Application Instances**: Load balancing across several FHIR server processes
- **Auto-scaling**: Dynamic instance creation/destruction based on load
- **Blue/Green Deployments**: Multiple versions running simultaneously during updates
- **High Concurrency**: Systems often batch SearchParameter updates

## Decision

We will implement a **two-layer concurrency control strategy** combining application-level pessimistic locking with database-level optimistic concurrency:

### Layer 1: Application-Level Concurrency Management
Implement `SearchParameterConcurrencyManager` - a static, per-SearchParameter-URI locking mechanism that ensures only one operation per SearchParameter can execute at a time within a single application instance.

**Implementation Details:**
- **Static ConcurrentDictionary**: Maps SearchParameter URIs to SemaphoreSlim instances
- **Per-URI Locking**: Each SearchParameter URI gets its own exclusive lock
- **Automatic Cleanup**: Unused semaphores are disposed to prevent memory leaks
- **Scope**: Protects all SearchParameter operations (Add, Update, Delete, Status changes)

### Layer 2: Database-Level Optimistic Concurrency  
Enhance the SQL schema (Version 94) with LastUpdated-based optimistic concurrency control in the SearchParam table to prevent conflicts between different application instances.

**Implementation Details:**
- **LastUpdated Column**: Use existing `LastUpdated` column in SearchParam table for change tracking
- **Enhanced Table Types**: Create `SearchParamTableType_3` with LastUpdated support
- **Updated Stored Procedures**: Modify `UpsertSearchParams` to detect and handle concurrency conflicts
- **Backward Compatibility**: Graceful fallback for schemas without LastUpdated-based optimistic concurrency support

### Integration Architecture
```
HTTP Request ? CreateOrUpdateSearchParameterBehavior ? SearchParameterOperations
                                                           ?
                                              SearchParameterConcurrencyManager (Application Lock)
                                                           ?
                                              [SearchParam Operations: Validate, Update Status, Update Definitions]
                                                           ?
                                              SqlServerSearchParameterStatusDataStore (Database Optimistic Concurrency)
                                                           ?
                                              SQL Server with LastUpdated checks
```

### Key Components Modified:

1. **SearchParameterConcurrencyManager** (New)
   - Static manager providing per-URI exclusive access
   - Methods: `ExecuteWithLockAsync<T>()` and `ExecuteWithLockAsync()`
   - Properties: `ActiveLockCount` for monitoring

2. **SearchParameterOperations** (Enhanced)
   - All methods wrapped with `SearchParameterConcurrencyManager.ExecuteWithLockAsync()`
   - Ensures atomic updates to both status and in-memory definitions

3. **SQL Schema Version 94** (New)
   - Enhanced `SearchParam` table using existing `LastUpdated` column for optimistic concurrency
   - New `SearchParamTableType_3` table type with LastUpdated support
   - Updated `UpsertSearchParams` stored procedure with conflict detection

4. **SqlServerSearchParameterStatusDataStore** (Enhanced)
   - Schema-aware operations supporting both V93 (without LastUpdated-based optimistic concurrency) and V94+ (with LastUpdated-based optimistic concurrency)
   - Defensive error handling during schema migration periods
   - Proper LastUpdated handling in GET and UPSERT operations

## Status
**Accepted** - Implemented and deployed

## Consequences

### Beneficial Effects

#### Eliminated Race Conditions
- **No More Concurrent Modifications**: Only one SearchParameter operation per URI can execute at any time
- **Atomic Operations**: SearchParameter status, in-memory cache, and database remain consistent
- **Reliable Error Handling**: Clear success/failure responses instead of cryptic exceptions

#### Enhanced Data Integrity
- **Cross-Instance Protection**: LastUpdated timestamp prevents conflicts between different FHIR server instances
- **Transactional Consistency**: All SearchParameter-related updates happen atomically
- **Schema Migration Safety**: Graceful handling of environments during schema upgrades

#### Improved Scalability
- **Per-URI Granularity**: Different SearchParameters can be modified concurrently
- **Minimal Lock Duration**: Locks held only during actual operations, not HTTP request lifetime  
- **Memory Efficient**: Automatic cleanup prevents semaphore accumulation

#### Better Observability
- **Monitoring Support**: `ActiveLockCount` property for health dashboards
- **Detailed Logging**: Lock acquisition/release events for troubleshooting
- **Clear Error Messages**: Specific concurrency conflict exceptions with affected URIs

### Adverse Effects

#### Increased Complexity
- **Two-Layer Architecture**: Developers must understand both application and database concurrency
- **Schema Versioning**: Additional migration complexity for LastUpdated-based optimistic concurrency
- **Error Handling**: More sophisticated exception handling for concurrency conflicts

#### Performance Considerations
- **Lock Contention**: High-frequency updates to the same SearchParameter may create bottlenecks
- **Database Overhead**: LastUpdated column checks add minimal overhead per SearchParam row
- **Memory Usage**: SemaphoreSlim instances consume additional memory (though minimal)

#### Deployment Dependencies
- **Schema Migration Required**: V94 database upgrade needed for full optimistic concurrency
- **Backward Compatibility Window**: Mixed behavior during schema migration period

### Neutral Effects

#### Existing Functionality Preserved
- **No Breaking Changes**: Existing SearchParameter operations continue to work unchanged
- **API Compatibility**: No changes to FHIR REST API surface
- **Performance Parity**: Similar performance characteristics for non-concurrent scenarios

#### Development Impact
- **Testing Strategy**: Enhanced integration tests needed for concurrency scenarios
- **Documentation**: Additional operational guidance for concurrency monitoring

### Edge Cases and Mitigation Strategies

#### High-Frequency Updates
- **Scenario**: Rapid-fire updates to the same SearchParameter
- **Impact**: Requests may experience increased latency due to queuing
- **Mitigation**: Client-side retry logic with exponential backoff

#### Schema Migration Period  
- **Scenario**: Mixed V93/V94 deployments during rolling updates
- **Impact**: Temporary fallback to application-level locking only
- **Mitigation**: Defensive programming with graceful degradation

#### Memory Pressure
- **Scenario**: Large numbers of distinct SearchParameter URIs
- **Impact**: Potential memory growth from cached semaphores
- **Mitigation**: Automatic cleanup of unused semaphores with lock-based cleanup to prevent races

#### Monitoring and Alerting
- **Lock Duration Monitoring**: Track average time spent waiting for SearchParameter locks
- **Concurrency Conflict Rates**: Monitor frequency of LastUpdated conflicts
- **Active Lock Count**: Alert if semaphore count grows unexpectedly

## References
- [FHIR SearchParameter Specification](https://hl7.org/fhir/searchparameter.html)
- [SQL Server datetimeoffset data type](https://docs.microsoft.com/en-us/sql/t-sql/data-types/datetimeoffset-transact-sql)
- [.NET SemaphoreSlim Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- Related ADRs:
  - ADR 2311: Limit Concurrent Calls to MergeResources (similar concurrency patterns)
  - ADR 2505: Eventual Consistency for Search Param Indexes (related to SearchParameter management)
