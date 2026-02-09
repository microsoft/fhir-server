# ReferenceResourceTypeId WHERE Clause Optimization

## Background

**PR**: [#5285 — Handle multiple target types for ReferenceSearchParam](https://github.com/microsoft/fhir-server/pull/5285)  
**Table**: `dbo.ReferenceSearchParam`  
**Branch**: `personal/jaerwin/reference-mulitple-targets`

### The Problem

FHIR search queries like `DiagnosticReport?subject=XXX` generate SQL against `dbo.ReferenceSearchParam` that does **not** include `ReferenceResourceTypeId` in the WHERE clause:

```sql
WHERE predecessorTable.SearchParamId = 414
    AND predecessorTable.ReferenceResourceId = @p0
    AND predecessorTable.ResourceTypeId = 40
```

The secondary index on this table is:

```
IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId
  (ReferenceResourceId, ReferenceResourceTypeId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId)
```

By omitting `ReferenceResourceTypeId` (column 2), the query optimizer cannot fully seek into this index — it can seek on `ReferenceResourceId` (column 1) but must then scan/filter across all `ReferenceResourceTypeId` values to reach `SearchParamId` (column 3). This produces suboptimal query plans on large datasets.

### The Proposed Fix (PR #5285)

The `UntypedReferenceRewriter` was updated to inject `ReferenceResourceTypeId` into the WHERE clause:
- **Single target type**: `AND ReferenceResourceTypeId = @type`
- **Multiple target types**: `AND (ReferenceResourceTypeId = @type1 OR ReferenceResourceTypeId = @type2)`

### The Complication: NULL Values

`ReferenceResourceTypeId` is defined as `smallint NULL`. It is set to NULL when:

1. **String references** — FHIR references stored as plain strings without a resource type prefix (e.g., `"abc"` instead of `"Patient/abc"`)
2. **External references** — References to external URLs where the type cannot be resolved

The C# code in `ReferenceSearchParamListRowGenerator.cs` (line 30):
```csharp
searchValue.ResourceType == null ? null : Model.GetResourceTypeId(searchValue.ResourceType)
```

There is also a TODO in the codebase acknowledging this design limitation:
> *"TODO: We should separate string references (ref resource type is null) from references to resources. This should be a long term fix."*

**Impact**: Adding `ReferenceResourceTypeId = @value` to the WHERE clause **silently excludes** rows where `ReferenceResourceTypeId IS NULL`, because `NULL = anything` evaluates to UNKNOWN in SQL. This is a **correctness bug** — valid search results may be dropped.

---

## Test Plan

A SQL performance test script has been created at:
[`docs/testing/ReferenceResourceTypeId_PerfTest.sql`](ReferenceResourceTypeId_PerfTest.sql)

### What the Test Does

1. **Creates a test database** (`FhirRefTypeIdTest`) with **three separate tables**, each with the exact `ReferenceSearchParam` schema and indexes (partitioning is omitted for simplicity — it maps all partitions to PRIMARY anyway). Each table has a different NULL distribution to cover unknown production data patterns:

   | Table | NULL % | Purpose |
   |-------|--------|---------|
   | `RefSearch_Null10` | 10% | Low-NULL scenario (mostly typed references) |
   | `RefSearch_Null30` | 30% | Medium-NULL scenario |
   | `RefSearch_Null60` | 60% | High-NULL scenario (many untyped string references) |

2. **Generates ~2M rows per table** (~6M total). Within the non-NULL portion of each table, typed references are distributed proportionally:

   | Segment | ReferenceResourceTypeId | % of Non-NULL Portion | Purpose |
   |---------|------------------------|-----------------------|---------|
   | Patient (103) | Non-NULL | 44% | Most common type |
   | Practitioner (104) | Non-NULL | 17% | Second common type |
   | Organization (105) | Non-NULL | 11% | Third type |
   | Device (106) | Non-NULL | 6% | Less common |
   | Group (107) | Non-NULL | 6% | Multi-target scenario |
   | Location (108) | Non-NULL | 6% | Additional type |
   | RelatedPerson (109) | Non-NULL | 5% | Additional type |
   | Medication (110) | Non-NULL | 5% | Additional type |

3. **Seeds specific test IDs in each table** for controlled scenarios:
   - **S1** (`common-patient-0001`): Common ID with many matching rows
   - **S2** (`rare-singleton-id`): Rare ID with 1-2 matching rows
   - **S3** (`overlap-typed-null-id`): ID that exists in BOTH typed and NULL rows
   - **S4** (`null-only-id`): ID that exists ONLY in NULL rows

4. **Runs 7 query variants × 4 scenarios × 3 NULL distributions** (84 total test executions):

   | Variant | WHERE Clause Pattern | Tests |
   |---------|---------------------|-------|
   | **Q1** (Baseline) | No ReferenceResourceTypeId filter | Current production behavior |
   | **Q2** (Single Type) | `ReferenceResourceTypeId = @val` | PR #5285 single-target case |
   | **Q3** (Multi-Type OR) | `(TypeId = @a OR TypeId = @b)` | PR #5285 multi-target case |
   | **Q4** (Single + NULL) | `(TypeId = @val OR TypeId IS NULL)` | Correctness-safe single target |
   | **Q5** (Multi + NULL) | `(TypeId = @a OR TypeId = @b OR TypeId IS NULL)` | Correctness-safe multi target |
   | **Q6** (IN Clause) | `TypeId IN (@a, @b)` | Alternative to OR syntax |
   | **Q7** (UNION ALL) | `... UNION ALL ... WHERE TypeId IS NULL` | Alternative NULL handling |

5. **Captures** for each execution:
   - Row count returned
   - Index used (secondary IXU vs clustered IXC)
   - Seek vs Scan operation
   - Estimated vs Actual rows (cardinality estimation accuracy)
   - Full execution plan XML (viewable in SSMS)

6. **Generates analysis reports**:
   - Correctness check (row count differences vs baseline Q1)
   - Index usage comparison across variants and NULL distributions
   - **Cross-distribution comparison** — shows whether increasing NULL % flips the optimizer's plan choice
   - Decision matrix with degradation risk assessment

### How to Run

1. Open SSMS and connect to a SQL Server 2019+ instance
2. Open `docs/testing/ReferenceResourceTypeId_PerfTest.sql`
3. Execute the entire script (or run Part 1, Part 2, Part 3 separately)
4. Part 1 creates 3 tables and loads ~6M rows total (may take several minutes)
5. Part 2 runs 84 test executions and stores results in `dbo.TestResults`
6. Part 3 outputs analysis reports — review printed output and query `dbo.TestResults`
7. Click on `ExecutionPlanXml` cells in SSMS to view graphical plans

### Decision Criteria

| Outcome | Decision |
|---------|----------|
| Q4/Q5 use index seeks AND row counts match baseline | ✅ Proceed with NULL-inclusive approach |
| Q4/Q5 degrade to scans while Q2/Q3 still seek | ❌ NULL-inclusive hurts; consider alternatives |
| Q4/Q5 seek at 10% NULL but scan at 60% NULL | ⚠️ Approach is fragile — depends on data distribution |
| Q2/Q3 miss rows that Q1 returns (S3/S4 scenarios) | ⚠️ Confirms correctness risk — must handle NULLs |
| Q7 (UNION ALL) gets two seeks at all NULL ratios | ✅ Preferred — immune to NULL distribution changes |
| Q1 baseline performs well enough across all scenarios | 🤔 Re-evaluate whether the optimization is needed |

---

## Test Results (2026-02-09)

### Data Distribution (Verified)

| Table | Total Rows | NULL Rows | NULL % | Patient Rows | Practitioner Rows |
|-------|-----------|-----------|--------|-------------|-------------------|
| RefSearch_Null10 | 2,000,010 | 199,915 | 10.0% | 779,391 | 300,709 |
| RefSearch_Null30 | 2,000,010 | 599,653 | 30.0% | 598,962 | 219,900 |
| RefSearch_Null60 | 2,000,010 | 1,198,664 | 59.9% | 340,541 | 119,471 |

Scenario IDs in Null10 table: `common-patient-0001` = 1,035 rows (107 NULL), `overlap-typed-null-id` = 5 rows (2 NULL), `null-only-id` = 3 rows (all NULL), `rare-singleton-id` = 2 rows (0 NULL).

### Key Finding: ALL Variants Fail Correctness

**Every query variant that adds `ReferenceResourceTypeId` to the WHERE clause loses rows compared to baseline (Q1):**

| Variant | S1 Rows | Baseline | Missing | Root Cause |
|---------|---------|----------|---------|------------|
| Q1-Baseline | 258 | — | — | Returns ALL rows (correct) |
| Q2-SingleType | 110 | 258 | **-148** | Excludes NULL rows AND other typed rows |
| Q3-MultiTypeOR | 145 | 258 | **-113** | Excludes NULL rows AND other typed rows |
| Q4-SingleType+NULL | 135 | 258 | **-123** | Includes NULL rows but still misses other typed rows |
| Q5-MultiType+NULL | 170 | 258 | **-88** | Includes NULL rows but still misses other typed rows |
| Q6-InClause | 145 | 258 | **-113** | Same as Q3 |
| Q7-UnionAll | 135 | 258 | **-123** | Same as Q4 |

**Critical insight**: The problem is NOT just about NULL rows. The `ReferenceResourceTypeId` column stores the type of the **referenced** resource, not the type expected by the search parameter. When a `DiagnosticReport.subject` reference points to a `Practitioner` (type 104), that row is stored with `ReferenceResourceTypeId = 104`. But if we filter with `ReferenceResourceTypeId = 103` (Patient only), we lose that Practitioner reference. The baseline Q1 (no type filter) correctly returns ALL references regardless of their target type.

This means the fundamental premise of PR #5285 has a data mismatch: the search parameter's `TargetResourceTypes` list does NOT necessarily match how references are actually stored. Real data contains references to types outside the search parameter's declared targets.

### Index Behavior

| Variant | Seeks (of 4 scenarios) | Index Used |
|---------|----------------------|------------|
| Q1-Baseline | 1 | Mixed — only seeks on S1 (many rows) |
| Q2-SingleType | 2 | IXU (secondary index) ✓ |
| Q3-MultiTypeOR | 2 | IXU (secondary index) ✓ |
| Q4-SingleType+NULL | 3 | IXU (secondary index) ✓ |
| Q5-MultiType+NULL | 3 | IXU (secondary index) ✓ |
| Q7-UnionAll | 2 | IXU (secondary index) ✓ |

All type-filtered variants successfully use the secondary index for seeks when rows exist. No degradation was observed across NULL distributions (10% → 30% → 60%).

### Cross-Distribution Comparison

Plans were **stable** across all three NULL ratios — the 60% NULL table did not cause the optimizer to switch from seeks to scans. This is a positive finding for the index, but irrelevant given the correctness failures.

### Conclusion

**⛔ Do NOT proceed with adding `ReferenceResourceTypeId` to the WHERE clause in its current form.**

The correctness failures are not limited to NULL handling — they are fundamental. Filtering by `ReferenceResourceTypeId` excludes valid references whose target type doesn't match the search parameter's declared target types. The `common-patient-0001` ID has 258 baseline rows but only 110 are Patient (type 103), meaning 148 rows reference other types (Practitioner, Organization, etc.) through the same search parameter.

### Recommended Path Forward

1. **Short term**: Leave the WHERE clause as-is (Q1 baseline). The index partial-seek on `ReferenceResourceId` alone is sufficient.
2. **Long term**: Pursue the **Backfill approach** (Alternative 1 below) — but this requires a deeper investigation into WHY references are stored with unexpected `ReferenceResourceTypeId` values. The data suggests that FHIR references to `subject` can point to any resource type, not just the declared targets.

---

## Alternative Approaches

### Alternative 1: Backfill NULL Values

**Description**: Run a one-time data migration to populate `ReferenceResourceTypeId` for all existing NULL rows, then make the column NOT NULL going forward.

**Implementation**:
1. Add a new schema migration version per [SchemaVersioning.md](../SchemaVersioning.md)
2. Create a migration script that infers the correct `ReferenceResourceTypeId` from the `ReferenceResourceId` and `SearchParamId` context
3. For rows where the type genuinely cannot be determined, either:
   - Assign a sentinel value (e.g., `-1`) to represent "unknown type"
   - Keep them NULL but add a separate filtered index for `WHERE ReferenceResourceTypeId IS NULL`
4. Update `ReferenceSearchParamListRowGenerator.cs` to always populate the column
5. Alter the column to `NOT NULL` (with default) once all rows are populated

**Pros**:
- Eliminates the NULL problem permanently — all future queries can use simple equality
- Index becomes fully seekable without OR/IS NULL predicates
- Cleanest long-term solution (aligns with the existing TODO in the code)
- No runtime query performance cost

**Cons**:
- Requires a schema version bump (backward compatibility considerations)
- Data migration on large tables can be slow and lock-intensive (needs careful batching)
- Must handle the "unknown type" case — what ResourceTypeId do you assign to truly untyped references?
- Risk: If the migration misassigns types, search results will be incorrect
- Cannot be deployed atomically — old code reading during migration may see inconsistent data

**Complexity**: High — requires schema migration, data backfill logic, rollback plan, and extensive testing.

**When to choose**: When the team is willing to invest in a permanent fix and the NULL rows can be reliably typed. This is the best long-term solution but the highest effort.

---

### Alternative 2: UNION ALL Approach

**Description**: Instead of `(ReferenceResourceTypeId = @val OR ReferenceResourceTypeId IS NULL)`, generate two separate queries joined with `UNION ALL`:

```sql
-- Typed references
SELECT ResourceTypeId, ResourceSurrogateId
FROM dbo.ReferenceSearchParam
WHERE ReferenceResourceTypeId = @type
  AND SearchParamId = @paramId
  AND ReferenceResourceId = @refId
  AND ResourceTypeId = @resTypeId

UNION ALL

-- Untyped (NULL) references
SELECT ResourceTypeId, ResourceSurrogateId
FROM dbo.ReferenceSearchParam
WHERE ReferenceResourceTypeId IS NULL
  AND SearchParamId = @paramId
  AND ReferenceResourceId = @refId
  AND ResourceTypeId = @resTypeId
```

**Implementation**:
1. Modify the SQL query generator (`SqlQueryGenerator.cs` / `SearchParameterQueryGenerator.cs`) to detect when a `ReferenceResourceTypeId` filter is being added
2. Instead of emitting an OR predicate, emit a UNION ALL of two CTE branches — one with the type equality and one with IS NULL
3. For multiple target types, the first branch becomes `ReferenceResourceTypeId IN (@type1, @type2)` and the second remains `IS NULL`

**Pros**:
- SQL Server can optimize each UNION ALL branch independently — each branch can do an index seek
- The IS NULL branch seeks on `(ReferenceResourceId, NULL, ...)` which is a valid seek into the B-tree (NULL sorts first in SQL Server indexes)
- No schema changes or data migration needed
- Preserves correctness — both typed and untyped rows are always returned
- Often produces better plans than OR predicates because the optimizer doesn't have to decide between seek and scan for a combined predicate

**Cons**:
- More complex SQL generation code — the CTE/query structure must accommodate two branches
- May produce duplicate rows if a `ReferenceResourceId` appears with both a matching type AND NULL (can be handled with UNION instead of UNION ALL, at slight dedup cost)
- Makes the generated SQL larger and harder to read/debug
- Interaction with the existing CTE-based query structure may be complex

**Complexity**: Medium — requires changes to the SQL generation layer but no schema changes.

**When to choose**: If the perf test shows that OR + IS NULL prevents index seeks, but UNION ALL achieves seeks on both branches. This is a common SQL optimization pattern.

---

### Alternative 3: Filtered Index for NULL Rows

**Description**: Add a secondary filtered index specifically for rows where `ReferenceResourceTypeId IS NULL`:

```sql
CREATE INDEX IX_ReferenceSearchParam_NullType
ON dbo.ReferenceSearchParam (ReferenceResourceId, SearchParamId, ResourceSurrogateId, ResourceTypeId)
WHERE ReferenceResourceTypeId IS NULL
WITH (DATA_COMPRESSION = PAGE);
```

**Implementation**:
1. Add a new schema migration version
2. Create the filtered index in the migration script
3. No changes to C# query generation needed — SQL Server can automatically use this index for queries with `ReferenceResourceTypeId IS NULL` predicates
4. The existing secondary index handles typed (non-NULL) queries; the filtered index handles NULL queries

**Pros**:
- Very targeted — only indexes the NULL rows, which are a small percentage (~10%)
- Small additional storage footprint
- SQL Server automatically considers filtered indexes when the WHERE clause matches
- No changes to query generation code needed (the optimizer picks the index)
- Can be combined with OR or UNION ALL approaches

**Cons**:
- Requires a schema version bump
- Filtered indexes have limitations:
  - Cannot be used with parameterized queries unless `OPTION (RECOMPILE)` is used or the parameter is sniffed correctly
  - The query must have a literal or compatible predicate `ReferenceResourceTypeId IS NULL` for the optimizer to consider the index
  - Parameterized `sp_executesql` queries may not match the filter
- Adds write overhead (one more index to maintain on every INSERT/UPDATE/DELETE)
- Does not help if the primary issue is the OR predicate on the existing index

**Complexity**: Low-Medium — simple schema change, but filtered index matching with parameterized queries can be unreliable.

**When to choose**: If NULL rows are queried independently (not mixed with typed queries), and the filtered index reliably matches. Best as a supplementary optimization, not a standalone solution.

---

### Alternative 4: Leave As-Is (No Change)

**Description**: Keep the current SQL generation without `ReferenceResourceTypeId` in the WHERE clause.

**Rationale**: The existing query works correctly (returns all rows regardless of type). The index can still do a partial seek on `ReferenceResourceId` (column 1). Adding `ReferenceResourceTypeId` only helps if the optimizer can leverage it for a deeper seek.

**When to choose**: If performance testing shows that the baseline (Q1) already produces acceptable plans and the additional complexity of handling NULLs does not justify the marginal index improvement. Also appropriate if the data is small enough that the performance difference is negligible.

---

## Recommendation Flow

```
Run perf test (docs/testing/ReferenceResourceTypeId_PerfTest.sql)
    │
    ├─ Check CROSS-DISTRIBUTION table: does NULL ratio flip plan choices?
    │     │
    │     ├─ Plans are STABLE across 10%/30%/60% NULL:
    │     │     │
    │     │     ├─ Q4/Q5 achieve index seeks?
    │     │     │     └─ YES → Use OR + IS NULL approach (simplest correct change)
    │     │     │
    │     │     ├─ Q7 (UNION ALL) achieves two seeks but Q4/Q5 don't?
    │     │     │     └─ YES → Use UNION ALL approach (Alternative 2)
    │     │     │
    │     │     └─ Neither OR nor UNION helps?
    │     │           └─ Consider Backfill (Alt 1) or Filtered Index (Alt 3)
    │     │
    │     └─ Plans DEGRADE at high NULL%:
    │           │
    │           ├─ Q7 (UNION ALL) is stable across all NULL ratios?
    │           │     └─ YES → Use UNION ALL (immune to distribution changes)
    │           │
    │           └─ All approaches degrade?
    │                 └─ Consider Backfill (Alt 1) to eliminate NULLs
    │
    └─ Q1 baseline is already fast enough at all NULL ratios?
          └─ Consider Leave As-Is (Alternative 4)
```

## Files

| File | Purpose |
|------|---------|
| [`ReferenceResourceTypeId_PerfTest.sql`](ReferenceResourceTypeId_PerfTest.sql) | SQL script: creates test DB, generates data, runs all query variants, captures results |
| This document | Test plan, alternatives analysis, decision criteria |

## Related Code

| File | Relevance |
|------|-----------|
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/UntypedReferenceRewriter.cs` | PR #5285 changes: adds ReferenceResourceType expressions for multi-target params |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/ReferenceQueryGenerator.cs` | Translates `FieldName.ReferenceResourceType` → `ReferenceResourceTypeId = @val` SQL |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SearchParameterQueryGenerator.cs` | Base class with `VisitMultiary` (handles OR expressions) and `VisitSimpleBinary` |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/ReferenceSearchParamListRowGenerator.cs` | Where NULL vs non-NULL `ReferenceResourceTypeId` is decided during data storage |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/ReferenceSearchParam.sql` | Table and index definitions |
