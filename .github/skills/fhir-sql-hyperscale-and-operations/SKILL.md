---
name: fhir-sql-hyperscale-and-operations
description: |
  Azure SQL Hyperscale operations for the Microsoft FHIR Server SQL data provider.
  Activate when: Azure SQL Hyperscale, read replica routing, "ReplicaTrafficRatio",
  "Accelerated Database Recovery", ADR, "page server", "HYPERSCALE", geo-replication,
  defrag operations, index rebuild, "WatchdogLeases", operational monitoring,
  "Online index operations", "RESUMABLE", "Transactions" table monitoring,
  "ResourceChangeData" partition maintenance, "CleanupEventLog", production deployment,
  zero-downtime migration, rolling upgrade, database sizing, IOPS tuning,
  "SWITCH PARTITION", "MERGE PARTITION", sliding window, RCSI.
---

# FHIR SQL Hyperscale and Operations

## Azure SQL Hyperscale Architecture Context

The FHIR Server schema was designed for Azure SQL Hyperscale from inception. Key architectural assumptions:

- **Storage scales to 128 TB** — Hyperscale raises the standard 4 TB (GP/BC) ceiling to 128 TB per single database (100 TB for elastic pools); it is not unlimited. **Target ≤ 70% utilization** (~90 TB / ~70 TB respectively) to preserve headroom for online index rebuilds, partition SPLIT/MERGE, and ADR version store spill
- **Compute scales independently** — vCore adjustments don't require data movement
- **Read replicas for offload** — configurable replica traffic ratio for read-heavy workloads
- **Near-instant backups** — snapshot-based, no I/O impact
- **Log service** — separate from compute, enables fast point-in-time restore
- **Page servers** — distributed storage layer, each handles ~128GB

### How Hyperscale Affects Schema Decisions

| Feature | Hyperscale Behavior | FHIR Server Implication |
|---------|--------------------|------------------------|
| Instant snapshots | Zero-copy backups | `ResourceChangeData` sliding window can use snapshots for historical analysis |
| Compute scaling | vCore changes in ~1 min | Reindex/export jobs adapt to available compute |
| Read replicas | Up to 4 replicas | `ReplicaTrafficRatio` routes search traffic to replicas |
| Page server fan-out | Automatic | Large EAV tables distribute across page servers naturally |
| Local SSD cache | Per compute node | Hot partitions (Patient, Observation) cache locally |

## Read Replica Routing (ReplicaTrafficRatio)

The `dbo.Parameters` table controls read replica routing:

```sql
-- Check current routing ratio
SELECT Number FROM dbo.Parameters WHERE Id = 'ReplicaTrafficRatio'
-- 0 = all traffic to primary
-- 1 = all eligible traffic to replicas
-- 0.5 = 50% split
```

### What Goes to Replicas
- `SearchImpl` calls with `readOnly: true` (all searches)
- `GetResourcesByTypeAndSurrogateIdRange` (bulk export)
- `FetchResourceChanges` (change feed polling)

### What Stays on Primary
- All writes (`MergeResources`)
- `DequeueJob` / `EnqueueJobs` (JobQueue operations)
- `AcquireWatchdogLease` / `RenewWatchdogLease`
- `UpdateResourceSearchParams` (reindex)
- `MergeResourcesBeginTransaction` / `CommitTransaction`

**Important**: Replica lag can affect search-after-write consistency. The `Transactions` table visibility protocol ensures read-your-writes within the primary, but replicas may lag by milliseconds to seconds.

## Accelerated Database Recovery (ADR)

ADR is **required** for Hyperscale and is enabled by default. Key implications for the FHIR schema:

### Why ADR Matters for FHIR Server

1. **Long-running exports** (`GetResourcesByTypeAndSurrogateIdRange` with 20-min timeout) — ADR prevents log truncation blocking and version store bloat.
2. **Large transactions** (`MergeResources` batches) — ADR's persistent version store handles large write sets without inflating the transaction log.
3. **Rollback performance** — Failed bulk imports roll back instantly via logical revert, not undo log replay.

### ADR Considerations

- **Version store size**: Monitor `sys.dm_tran_persistent_version_store_stats`. Large reindex or export operations can grow this.
- **PVS cleanup**: ADR has background cleanup. If cleanup falls behind, it shows as PVS growth.
- **TempDB**: ADR uses tempDB for version store spilling. Monitor tempDB contention during heavy write loads.

```sql
-- Monitor ADR version store
SELECT 
    database_id,
    persistent_version_store_size_kb / 1024 / 1024 AS pvs_size_mb,
    online_index_version_store_size_kb / 1024 / 1024 AS online_index_pvs_mb,
    abort_transaction_count,
    oldest_active_transaction_id
FROM sys.dm_tran_persistent_version_store_stats;
```

## Query Store Configuration for FHIR Workloads

Recommended Query Store settings for the FHIR Server:

```sql
-- Query Store should be enabled for plan analysis
ALTER DATABASE CURRENT SET QUERY_STORE = ON;
ALTER DATABASE CURRENT SET QUERY_STORE (
    OPERATION_MODE = READ_WRITE,
    CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30),
    DATA_FLUSH_INTERVAL_SECONDS = 900,
    MAX_STORAGE_SIZE_MB = 1000,
    INTERVAL_LENGTH_MINUTES = 15,
    SIZE_BASED_CLEANUP_MODE = AUTO,
    QUERY_CAPTURE_MODE = AUTO,  -- or CUSTOM with filtered predicates
    MAX_PLANS_PER_QUERY = 50,
    WAIT_STATS_CAPTURE_MODE = ON
);
```

### Query Store Capture Mode: AUTO vs ALL

- **AUTO** (recommended): Ignores infrequent and trivial queries. Good for production.
- **ALL**: Captures everything including `DequeueJob` heartbeat queries. Use only for deep debugging.
- **CUSTOM**: Use for targeted capture during known performance incidents.

## Operational Monitoring Queries

### Partition Health

```sql
-- Check partition sizes and row counts per resource type
SELECT 
    p.partition_number,
    OBJECT_NAME(p.object_id) AS table_name,
    prv.value AS boundary_value,
    p.rows AS row_count,
    CAST(a.used_pages * 8.0 / 1024 AS decimal(10,2)) AS used_mb
FROM sys.partitions p
JOIN sys.allocation_units a ON p.partition_id = a.container_id
LEFT JOIN sys.partition_range_values prv ON p.partition_number = prv.boundary_id + 1
WHERE p.object_id IN (
    OBJECT_ID('dbo.Resource'),
    OBJECT_ID('dbo.TokenSearchParam'),
    OBJECT_ID('dbo.StringSearchParam'),
    OBJECT_ID('dbo.DateTimeSearchParam')
)
AND p.index_id IN (0, 1)  -- clustered index or heap
ORDER BY table_name, partition_number;
```

### Index Fragmentation

```sql
-- Fragmentation on search param tables (critical for seek performance)
SELECT 
    OBJECT_NAME(object_id) AS table_name,
    name AS index_name,
    index_type_desc,
    partition_number,
    avg_fragmentation_in_percent,
    page_count
FROM sys.dm_db_index_physical_stats(
    DB_ID(), 
    NULL,  -- all tables
    NULL,  -- all indexes
    NULL,  -- all partitions
    'LIMITED'
)
WHERE avg_fragmentation_in_percent > 30
  AND page_count > 1000
ORDER BY avg_fragmentation_in_percent DESC;
```

### Lock Contention Hotspots

```sql
-- Identify partition-level lock waits
SELECT 
    OBJECT_NAME(resource_associated_entity_id) AS table_name,
    request_mode,
    request_status,
    request_session_id,
    blocking_session_id,
    wait_time_ms,
    resource_type
FROM sys.dm_tran_locks l
LEFT JOIN sys.dm_os_waiting_tasks w ON l.request_session_id = w.session_id
WHERE resource_type IN ('OBJECT', 'PAGE', 'KEY', 'RID')
  AND OBJECT_NAME(resource_associated_entity_id) LIKE '%SearchParam%'
ORDER BY wait_time_ms DESC;
```

### ResourceChangeData Sliding Window Health

```sql
-- Check partition boundaries for ResourceChangeData
SELECT 
    pf.name AS partition_function,
    prv.boundary_id,
    CONVERT(datetime2, prv.value) AS boundary_datetime,
    p.rows
FROM sys.partition_functions pf
JOIN sys.partition_range_values prv ON pf.function_id = prv.function_id
JOIN sys.partitions p ON p.partition_number = prv.boundary_id + 1
WHERE pf.name LIKE '%ResourceChangeData%'
ORDER BY prv.boundary_id;
```

## Maintenance Procedures

### Defrag and Index Rebuild

The schema includes `DefragChanges` and related procedures under Watchdog coordination:

```sql
-- Check if index rebuild is enabled
SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.IndexRebuild.IsEnabled'
-- Check cleanup config
SELECT Number FROM dbo.Parameters WHERE Id = 'CleanupEventLog.Days'
```

**Operational rules**:
- All index maintenance must use `WITH (ONLINE = ON)` — blocking a partition blocks all resource types in that partition
- `RESUMABLE = ON` where supported (SQL Server 2019+) for interruption tolerance
- Watchdog leases ensure only one instance runs maintenance at a time
- `Rebuild` threshold: typically > 30% fragmentation on indexes > 1000 pages
- `Reorganize` threshold: 5-30% fragmentation

### EventLog Cleanup

```sql
-- Check EventLog size
SELECT COUNT(*) FROM dbo.EventLog;
SELECT MIN(LogDate) AS oldest_entry, MAX(LogDate) AS newest_entry FROM dbo.EventLog;

-- Parameters control cleanup
SELECT * FROM dbo.Parameters WHERE Id LIKE 'CleanupEventLog%';
```

EventLog grows unbounded. The `CleanupEventLog.*` parameters must be tuned per deployment. Over-logging in new procedures is a known storage risk.

### ResourceChangeData Partition Maintenance

The sliding window for `ResourceChangeData` requires periodic:
1. **SPLIT** at future boundary (pre-allocate)
2. **SWITCH OUT** old partition to staging table
3. **MERGE** old boundary
4. **TRUNCATE** staging table

This is orchestrated by Watchdog-triggered procedures. Manual intervention should only be needed if:
- Partitions run out (future boundary not split)
- SWITCH fails due to data integrity issues
- Staging table not truncated (storage leak)

## Zero-Downtime Migration Protocol

The schema supports rolling upgrades via `SchemaVersionConstants.Min` and `Max`:

```csharp
public const int Min = N;  // Oldest supported schema version
public const int Max = N+1; // Current schema version
```

**Upgrade sequence**:
1. New app version deployed (handles both N and N+1 schema)
2. Migration diff.sql runs (idempotent, online operations)
3. `Max` bumped to N+1
4. Old app instances drained (they only support up to N)
5. `Min` bumped to N+1 (after all old instances gone)

**Critical**: Never drop old procedure versions or make columns `NOT NULL` until `Min` has advanced past the version that needed them.

## Production Troubleshooting Runbook

### Symptom: Search queries suddenly slow
1. Check Query Store for plan regressions (`sys.query_store_plan` with multiple plans for same query)
2. Check statistics last update: `sys.stats` + `STATS_DATE()`
3. Check for partition elimination failure in recent plans
4. Check `ReplicaTrafficRatio` — if recently changed, replica routing may cause plan cache misses
5. Check `CustomQueries` table for unexpected overrides

### Symptom: Write throughput degraded
1. Check `Transactions` table for uncommitted transactions (stuck `MergeResourcesCommitTransaction`)
2. Check `JobQueue` for blocked dequeue operations (`sp_getapplock` contention)
3. Check `WatchdogLeases` — expired leases indicate instance failure
4. Check `EventLog` for error clusters from `MergeResources`
5. Check ADR PVS size — large version store slows writes

1. **Alert threshold**: Flag when storage exceeds 70% of tier ceiling (~90 TB single DB, ~70 TB elastic pool) — leave headroom for online rebuilds, partition ops, and ADR PVS spill
1. Check `ResourceChangeData` partition count (should not exceed retention window)
2. Check `EventLog` row count and retention settings
3. Check for `InvisibleHistory.IsEnabled` — if disabled, historical versions store full blobs
4. Check `StringSearchParam` and `TokenText` table growth (text search indexes expand faster)
5. Check `dbo.QuantityCode` and `dbo.System` for unbounded growth (rare, but possible with custom codes)

### Symptom: Reindex job failing
1. Check `JobQueue` status and heartbeat for reindex job
2. Check `SearchParamHash` mismatch rate — high rate means more work per batch
3. Check `UpdateResourceSearchParams` duration in `EventLog`
4. Check tempDB usage during reindex (large sort operations)
5. Verify `MaxItemCount` on reindex batches isn't too large (memory pressure)

## Checklist for Operational Recommendations

Before suggesting any infrastructure change:
- [ ] Does the change respect `LOCK_ESCALATION = AUTO` on partitioned tables?
- [ ] Are index operations specified with `WITH (ONLINE = ON)`?
- [ ] Does the change work under both RCSI and ADR?
- [ ] Is `ReplicaTrafficRatio` considered for read vs write impact?
- [ ] Does the maintenance fit within Watchdog lease duration?
- [ ] Will the change affect backward compatibility (Min/Max contract)?
- [ ] Is EventLog growth considered for new logging?
- [ ] Are partition SWITCH/MERGE/SPLIT operations instant metadata ops only?
- [ ] Does the recommendation account for Hyperscale page server distribution?
- [ ] Is database storage below 70% of the tier ceiling (~90 TB for single DB, ~70 TB for elastic pool)?
