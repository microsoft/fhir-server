---
name: fhir-sql-performance-diagnostics
description: |
  Diagnose and optimize slow queries in the Microsoft FHIR Server SQL data provider.
  Activate when: slow FHIR search, query plan analysis, "query timeout", "partition elimination",
  "index scan", "parameter sniffing", "cardinality estimate", "table scan", "hash join",
  "nested loop", "deadlock", "blocking", "InvalidSearchOperationException",
  "SearchParameterTooComplexException", "QueryProcessorNoQueryPlan", "query store",
  "CustomQueries", "query hash", "Search.ReuseQueryPlans", "OPTIMIZE FOR UNKNOWN",
  "MAXDOP 1", "@DummyTop", "filtered index", "IsHistory = 0", "ResourceSurrogateId range",
  "CTE performance", "include performance", "chain search slow", "sort performance",
  "_total=accurate", bulk export slow, reindex performance.
---

# FHIR SQL Performance Diagnostics

## Diagnostic Philosophy

**Project design decisions override generic SQL best practices.** The FHIR Server schema makes deliberate tradeoffs (partition-per-resource-type, EAV side tables, CTE chains, `varbinary(max)` blobs) that generic SQL advice often contradicts. Always validate recommendations against the schema's invariants.

## Diagnostic Verification Protocol

For EVERY performance diagnosis, verify against actual evidence:

1. **If claiming "missing partition elimination"**:
   - Get the ACTUAL generated SQL
   - Search for `ResourceTypeId`
   - If FOUND: Check if `IN (all types)` → design limitation, NOT bug
   - If NOT FOUND: Bug — rewriter was bypassed

2. **If recommending a code change**:
   - Read the ACTUAL C# file
   - Verify your understanding matches the code

**Never diagnose based on skill abstractions alone. Always verify with actual SQL, query plans, or C# source.**

## The Diagnostic Workflow

### Step 1: Identify the Query Category

| Category | Typical Duration | Likely Cause |
|----------|-----------------|--------------|
| Single resource by `_id` | < 10ms | Should hit `IX_Resource_ResourceTypeId_ResourceSurrogateId` |
| Resource type search, no params | < 100ms | Simple scan of one partition |
| Resource type + 1-2 search params | 100ms - 2s | CTE chain over side tables |
| Complex search + `_include` | 2s - 30s | Include JOIN explosion or missing sort optimization |
| Chain search (`subject:Patient.name`) | 1s - 60s+ | Multi-level reference traversal |
| System-wide search (`/`) | 40x slower | Cross-partition scan (Issue #3021) — design limitation |
| `_sort` on non-`_lastUpdated` | 2x - 5x slower | Two-phase sort protocol |
| `CountOnly` with `_include` | Unnecessarily slow | `_include` discarded but expressions still parsed |

### Step 2: Verify Partition Elimination (CRITICAL — Two Scenarios)

**Scenario A: TRULY Missing ResourceTypeId — CATASTROPHIC BUG**
- Symptom: Query scans all 147 partitions, 40x slower than single-type
- Root cause: NO `ResourceTypeId` predicate exists in the SQL — the `PartitionEliminationRewriter` was bypassed or a custom query omitted it
- How to check: Query plan shows `Partition Count = 147+` AND the SQL has NO `ResourceTypeId` anywhere in WHERE/JOIN
- Fix: Fix the C# rewriter pipeline or custom SQL

**Scenario B: System-Wide Search with ResourceTypeId IN(all types) — DESIGN LIMITATION**
- Symptom: `GET /` or system-wide searches take 5-10+ minutes on large databases (~6min49s documented in Issue #3021 on 4TB)
- Root cause: `PartitionEliminationRewriter` correctly injects `ResourceTypeId IN (all 147 types)` — touching ALL partitions is inherently slow, not a bug
- How to check: SQL DOES contain `ResourceTypeId IN (...)` — partition elimination IS working, it's just eliminating nothing because all types are requested
- This is NOT a bug — it's a known design limitation of partition-per-resource-type schema
- DO NOT recommend "fixing" `PartitionEliminationRewriter` for this case
- Mitigation: Encourage `_type` filters; system-wide searches on multi-TB databases will always be slow

### Step 3: Check CTE Chain Health

Healthy CTE chain characteristics:
- Each CTE filters the predecessor (narrowing row count)
- `Nested Loops` joins between CTEs (small outer, indexed inner)
- No `Hash Match` joins on CTE-to-CTE joins (indicates cardinality misestimate)

Unhealthy patterns:
- Early CTE returns large row count, later CTEs try to filter it (should reorder)
- `Sort` operator before `Top` on large intermediate result
- `Spool` or `Lazy Spool` in the plan (usually from correlated subqueries)

### Step 4: Examine Search Param Table Access

For each side table access (TokenSearchParam, StringSearchParam, etc.):

| Access Pattern | Expected Plan | Risk |
|----------------|--------------|------|
| `SearchParamId = @id AND Code = @code` | Index seek on `IX_TokenSearchParam_SearchParamId_Code` | None |
| `SearchParamId = @id AND Code LIKE 'prefix%'` | Index seek with range scan | None |
| `SearchParamId = @id AND Text LIKE '%suffix'` | Index scan (LIKE with leading wildcard) | Slow on large partitions |
| `SearchParamId = @id` only (no value predicate) | Index seek, returns all rows for that param | May return huge set |
| Missing `SearchParamId` predicate | Clustered index scan on entire partition | Catastrophic |

### Step 5: Verify Sort and Top Placement

- `_lastUpdated` sort: Should use `ResourceSurrogateId ORDER BY` — no sort operator needed (data is clustered in this order)
- Non-`_lastUpdated` sort: Check for `Sort` operator in the plan. If sorting millions of rows, the `SortRewriter` two-phase protocol may be mispredicting selectivity.
- `Top` placement: Should be AFTER all filters and sorts. If `Top` appears early with `Sort` later, pagination will be wrong.

## Optimizer Hints: When and Why

### `@DummyTop` + `OPTION (OPTIMIZE FOR (@DummyTop = 1))`

```sql
DECLARE @DummyTop bigint = 9223372036854775807
SELECT TOP (@DummyTop) T1, Sid1 FROM ...
WHERE ...
OPTION (OPTIMIZE FOR (@DummyTop = 1))
```

**Purpose**: Compile-time cardinality of 1 row → optimizer chooses nested loop joins and index seeks. At runtime all rows are returned, but the plan shape is optimized for the common case (small result sets).

**When to use**: Search queries where typical result count is small (< 1000) but could theoretically be large. All search param CTEs use this.

**When NOT to use**: Bulk export queries (`GetResourcesByTypeAndSurrogateIdRange`) that always return large sets — these use stored procedures with `@DummyTop` but different optimization goals.

### `OPTION (OPTIMIZE FOR UNKNOWN)`

```sql
-- In SqlQueryGenerator.AddOptionClause()
-- Triggered when: >1 search param, one is "identifier", and query has _include
OPTION (OPTIMIZE FOR UNKNOWN)
```

**Purpose**: When the optimizer sees `identifier` + complex includes, it makes poor cardinality estimates based on parameter values. `OPTIMIZE FOR UNKNOWN` forces generic plans.

**When to add**: If query plans show wild cardinality misestimates specifically for identifier+include combinations.

**When NOT to add**: Simple queries with stable data distributions — this removes all parameter value information from plan compilation.

### `WITH (INDEX = IX_Resource_ResourceTypeId_ResourceSurrgateId)`

```sql
-- Applied in SqlQueryGenerator for simple resource-type-only searches
FROM Resource r WITH (INDEX = IX_Resource_ResourceTypeId_ResourceSurrgateId)
```

**Purpose**: Prevents the optimizer from choosing a clustered index scan on the Resource table for simple `GET /Patient` style queries.

**When to add**: Any direct `dbo.Resource` access that should use the `(ResourceTypeId, ResourceSurrogateId)` nonclustered index.

**When NOT to add**: Queries that already have search param CTEs — the CTE-to-Resource JOIN should use the clustered index (the CTE provides specific SurrogateIds).

### `MAXDOP 1`

```sql
DELETE FROM TokenSearchParam WHERE ResourceTypeId = @RT AND ResourceSurrogateId = @RSID
OPTION (MAXDOP 1)
```

**Purpose**: Prevents parallel plan overhead on inherently serial operations (single-row DML, queue dequeue).

**When to use**: Single-row lookups, queue operations (`DequeueJob`), small-batch DML.

**When NOT to use**: Large scans, bulk exports, reindex operations — these benefit from parallelism.

### `OPTION (RECOMPILE)`

Used in two places:
1. SmartV2 scope include regeneration (rebuilds filtered data for include phase)
2. Custom query override point in `CustomQueries`

**When to add**: Queries with extreme parameter-value sensitivity where plan reuse causes bad plans.

**When NOT to add**: Hot-path queries where compilation overhead matters — use parameter hashing instead.

## Design Limitations vs Bugs: How to Tell the Difference

### Bug Checklist (recommend code fix)
- [ ] Generated SQL differs from what the rewriter is SUPPOSED to emit
- [ ] Query plan contradicts documented intent
- [ ] Single-type search is unexpectedly slow (should be fast)

### Design Limitation Checklist (do NOT recommend code fix)
- [ ] Generated SQL matches documented intended behavior
- [ ] "Problem" is inherent to schema choice (EAV, 147 partitions, CTE chains)
- [ ] Fixing would require schema redesign, not a code tweak

### Known Design Limitations (NOT bugs)
| Limitation | Why Inherent | Recommend Instead |
|-----------|-------------|-----------------|
| System-wide search slow | 147 partitions touched | Add `_type` filters |
| Chain depth > 2 slow | No materialized paths | Pre-compute in app |
| Non-`_lastUpdated` sort 2-5x slower | Two-phase protocol required | Combine with filter |
| Token `:text` search slow | `CI_AI` full scan | Use exact matches |

**Critical rule**: If code does exactly what the skill says it should do, but result is slow, this is a DESIGN LIMITATION. Do NOT recommend "fixing" the rewriter.

## Known GitHub Issues and Their Findings

### Issue #3021: System-Wide Search Performance on Large Databases
**Finding**: On 4TB+ database, system-wide search takes ~6 minutes 49 seconds vs ~9.3 seconds for single-type.
**Root cause**: NOT missing partition elimination. `PartitionEliminationRewriter` correctly injects `ResourceTypeId IN (all 147 types)`. Touching ALL partitions is inherently slow.
**Key insight**: The all-types IN list is working as designed. Slowness is proportional to data volume.
**Resolution**: Classified as design limitation. Mitigation: use `_type` filters.
**What to avoid**: Do NOT diagnose this as "missing partition elimination" — partition elimination IS working, it's just not helpful when all types are requested.

## Common Anti-Patterns (WRONG recommendations agents make)

### WRONG: "Add a covering index on Resource for all search columns"
The Resource table stores compressed `varbinary(max)` blobs. Adding wide indexes defeats the purpose of EAV extraction. Search parameters are in side tables for a reason.

### WRONG: "Use temporal tables instead of IsHistory + Version"
Temporal tables cannot express the `RawResource = 0xF` invisible history sentinel. The custom history model is required for FHIR semantics.

### WRONG: "Remove the EAV tables and use JSON path indexes"
Until SQL Server 2025 native JSON is proven faster than compressed blobs for this workload, the EAV model stays. JSON path indexes would require massive rearchitecture.

### WRONG: "Use memory-optimized tables for JobQueue"
Memory-optimized tables have constraints (no FKs, limited index types) that conflict with the schema patterns. The 16-partition hash design already distributes contention.

### WRONG: "Add NOLOCK hints for read performance"
The schema relies on RCSI (default on Azure SQL). `NOLOCK` would return uncommitted transaction data — the `Transactions` table visibility protocol would be bypassed.

### WRONG: "Replace CTEs with temp tables for better performance"
The CTE chain is required for:
- Sort value propagation through JOINs
- Include phase separation via `@FilteredData`
- Union/Intersect logic for complex expressions
Temp tables would break the visitor pattern and require procedural SQL.

### WRONG: "Add a datetime2 column index on Resource for _lastUpdated queries"
`_lastUpdated` is encoded in `ResourceSurrogateId`. Adding a datetime index would be redundant and waste space. Date range queries use surrogate ID ranges.

## Slow Query Categories and Fixes

### Category A: Truly Missing Partition Elimination (BUG)
**Symptom**: Query scans all 147 partitions, 40x slower.
**Root cause**: `ResourceTypeId` not in WHERE clause or CTE predicate — rewriter was bypassed.
**Fix**: Check `PartitionEliminationRewriter` pipeline. For custom SQL, always include `ResourceTypeId`.

### Category A2: System-Wide Search Inherently Slow (DESIGN LIMITATION, NOT A BUG)
**Symptom**: `GET /` takes 5-10+ minutes on large DBs.
**Root cause**: `PartitionEliminationRewriter` correctly injects `ResourceTypeId IN(all 147 types)` — all partitions touched.
**How to verify**: SQL contains `ResourceTypeId IN (...)`. Query plan shows `Partition Count = 147`.
**This is NOT a bug**: Do NOT recommend fixing `PartitionEliminationRewriter`.
**Known issue**: Issue #3021 documents this (~6min49s on large DB).
**Mitigation**: Encourage `_type` filters. For true system-wide needs, consider read replica offload or application-level aggregation.

### Category B: Include Join Explosion
**Symptom**: `_include` query returns in seconds but includes take minutes.
**Root cause**: Include CTEs JOIN `ReferenceSearchParam` → `Resource` without adequate filtering.
**Fix**: Check `IncludeLimit` CTEs are present. Verify `IncludeQueryGenerator` emits `TOP (@IncludeCount+1)` per include path. If include targets are high-cardinality references, consider reducing `IncludeCount`.

### Category C: Chain Search Depth > 2
**Symptom**: `subject:Patient.name=Smith` or deeper chains timeout.
**Root cause**: Each chain level adds a `ReferenceSearchParam` JOIN `Resource` CTE. Depth > 2 creates multi-level nested loops.
**Fix**: Chains are inherently expensive. The schema does not materialize reference paths. Consider:
- Pre-computing common chains in application layer
- Using `_has` (reverse chain) when the target cardinality is lower
- Adding `SearchParamTableExpressionReorderer` selectivity hints if the chain target is more selective than the source

### Category D: Sort on Low-Selectivity Parameter
**Symptom**: `_sort=-issued` or `_sort=given` on a large resource type returns slowly; removing `_sort` makes it fast.
**Root cause**: Two-phase sort protocol scans all rows for the sort param in `DateTimeSearchParam`/`StringSearchParam`, then sorts before `TOP N`. **Compounding gap**: All NC indexes on `DateTimeSearchParam` store `StartDateTime ASC` only and exclude `ResourceSurrogateId` from `INCLUDE`. This means every sort row needs:
  1. A forward scan (no DESC index), producing a `Sort` operator on millions of rows, AND
  2. A clustered index lookup (no `ResourceSurrogateId` in NC index) to resolve the surrogate for the JOIN back to the filter CTE.
**Fix options**:
- If the sort parameter is also a filter (`date=2024&_sort=-date`), `SortWithFilter` is used (single phase, much faster). Encourage combining sort with filter.
- For production sort-heavy workloads: add a DESC index with `ResourceSurrogateId` in `INCLUDE`:
  ```sql
  CREATE INDEX IX_SearchParamId_StartDateTime_DESC_INCLUDE_ResourceSurrogateId
  ON dbo.DateTimeSearchParam (SearchParamId, StartDateTime DESC)
  INCLUDE (ResourceSurrogateId)
  WITH (DATA_COMPRESSION = PAGE)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId);
  ```
- Otherwise, two-phase sort without a covering DESC index is a known design gap for large datasets.

### Category E: Count with Complex Predicates
**Symptom**: `_total=accurate` on complex query is very slow.
**Root cause**: Count executes a second query (`CountOnly = true`) that discards `_include` but still evaluates all predicates.
**Fix**: The count query removes includes via `RemoveIncludesRewriter`. If still slow, the base predicate CTE chain is the bottleneck — apply Category A/B/C fixes.

### Category F: Token Text Search (`:text` modifier)
**Symptom**: `code:text=diabetes` is slow.
**Root cause**: Token text uses `TokenText` table with `CI_AI` collation, full scan on text values.
**Fix**: Token text is inherently expensive. The `:text` modifier is for human-readable search, not code-exact. If performance is critical, prefer exact token matches.

## Query Store Analysis for FHIR Workloads

Azure SQL Query Store is the primary tool for historical performance analysis.

### Key Queries

```sql
-- Top 20 slow queries by duration
SELECT TOP 20 
    qs.query_id, qs.query_hash, 
    rs.avg_duration/1000 AS avg_duration_ms,
    rs.count_executions,
    p.query_plan
FROM sys.query_store_query qs
JOIN sys.query_store_plan p ON qs.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
WHERE rs.last_execution_time > DATEADD(hour, -24, GETUTCDATE())
ORDER BY rs.avg_duration DESC;

-- Find queries with multiple plans (parameter sniffing indicator)
SELECT query_id, COUNT(DISTINCT plan_id) AS plan_count
FROM sys.query_store_plan
GROUP BY query_id
HAVING COUNT(DISTINCT plan_id) > 5
ORDER BY plan_count DESC;

-- Queries with regressed plans
SELECT 
    qsq.query_id,
    qsrs1.avg_duration AS recent_avg_ms,
    qsrs2.avg_duration AS older_avg_ms
FROM sys.query_store_query qsq
JOIN sys.query_store_plan qsp1 ON qsq.query_id = qsp1.query_id
JOIN sys.query_store_runtime_stats qsrs1 ON qsp1.plan_id = qsrs1.plan_id
JOIN sys.query_store_plan qsp2 ON qsq.query_id = qsp2.query_id
JOIN sys.query_store_runtime_stats qsrs2 ON qsp2.plan_id = qsrs2.plan_id
WHERE qsrs1.last_execution_time > DATEADD(hour, -1, GETUTCDATE())
  AND qsrs2.last_execution_time BETWEEN DATEADD(day, -7, GETUTCDATE()) AND DATEADD(hour, -1, GETUTCDATE())
  AND qsrs1.avg_duration > qsrs2.avg_duration * 5;
```

### FHIR-Specific Query Store Patterns

- Look for `/* HASH ... */` comments in query text — these are search queries
- Plans with `PartitionCount > 10` indicate partition elimination failure OR system-wide search (check if SQL has `IN (all types)`)
- Plans with `Nested Loops` + `Filter` on `ReferenceSearchParam` indicate chain queries
- High `logical reads` on `DateTimeSearchParam` with `Index Scan` suggests either: (a) missing `IsLongerThanADay` filtered index exploitation for range queries, or (b) a `_sort` query hitting the ascending NC index without a `ResourceSurrogateId` INCLUDE — requiring a clustered index lookup per row (all 4 NC indexes on `DateTimeSearchParam` lack `ResourceSurrogateId` in their INCLUDEs)

## CustomQueries Override Mechanism

When a specific query hash is consistently slow, the team can add a custom SQL override:

```csharp
// CustomQueries.cs
// Query hash → custom SQL text mapping stored in database
// Used for emergency query plan fixes without code deployment
```

**When to recommend**: 
- Query plan regression after statistics update
- Specific parameter value combinations that always get bad plans
- Emergency production fix when code deployment isn't possible

**When NOT to recommend**: 
- As a first-line solution — fix the root cause in the rewriter/generator
- For broad query categories — custom queries are one-off overrides
- For system-wide search slowness (Category A2) — this is a design limitation

## Long-Running Query Detection

`SqlServerSearchService` automatically logs queries exceeding `Search.LongRunningQueryDetails.Threshold` (default 5000ms):

```csharp
// Automatic fire-and-forget logging
if (executionStopwatch.ElapsedMilliseconds > threshold)
{
    // Logs query text + Query Store lookup for plan analysis
    LogQueryStoreByTextAsync(queryText, isStoredProc, ...);
}
```

**How to use this data**:
1. Check application logs for "Long-running SQL" warnings
2. Extract the query hash from the log
3. Look up the query in Query Store
4. Compare the plan with expected plan shape from this skill

## Checklist for Query Optimization Recommendations

Before recommending ANY change, verify:
- [ ] Does the query include `ResourceTypeId` for partition elimination? (Scenario A vs B)
- [ ] Is this a BUG or a DESIGN LIMITATION? (see Design Limitations vs Bugs section)
- [ ] Are search param table predicates using the correct index (seek vs scan)?
- [ ] Is the CTE chain narrowing row count progressively?
- [ ] Does the plan use nested loops for CTE joins (small outer)?
- [ ] For includes: is `IncludeLimit` present and effective?
- [ ] For sorts: is the two-phase protocol actually needed, or can `SortWithFilter` be used?
- [ ] Are filtered indexes being exploited where present (e.g., `TextOverflow IS NOT NULL` on `StringSearchParam`, `SingleValue IS NULL` vs `IS NOT NULL` on `NumberSearchParam`/`QuantitySearchParam`, `IsHistory = 0` on `Resource` and `TokenText`)?
- [ ] Is `DATA_COMPRESSION = PAGE` active on relevant indexes?
- [ ] Does the recommendation respect the expand-contract migration pattern?
- [ ] Would the recommendation work on both single-type and system-wide searches?
