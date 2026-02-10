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

1. **Creates a test database** (`FhirRefTypeIdTest`) with **three separate tables**, each with the exact `ReferenceSearchParam` schema and indexes. Each table has a different NULL distribution:

   | Table | NULL % | Purpose |
   |-------|--------|---------|
   | `RefSearch_Null10` | 10% | Low-NULL scenario (mostly typed references) |
   | `RefSearch_Null30` | 30% | Medium-NULL scenario |
   | `RefSearch_Null60` | 60% | High-NULL scenario (many untyped string references) |

2. **Generates ~20M rows per table** (~60M total). `ReferenceResourceTypeId` values are correlated to `SearchParamId` per FHIR R4 spec — each search parameter only gets target types that are valid for it.

3. **Seeds specific test IDs in each table** for controlled scenarios:
   - **S1** (`common-patient-0001`): Common ID with many matching rows
   - **S2** (`rare-singleton-id`): Rare ID with 1-2 matching rows
   - **S3** (`overlap-typed-null-id`): ID that exists in BOTH typed and NULL rows
   - **S4** (`null-only-id`): ID that exists ONLY in NULL rows

4. **Runs 2 query variants × 4 scenarios × 3 NULL distributions** (24 total test executions):

   | Variant | WHERE Clause Pattern | What It Tests |
   |---------|---------------------|---------------|
   | **Q1** (Baseline) | No `ReferenceResourceTypeId` filter | Current production behavior |
   | **Q2** (All Types + NULL) | `(TypeId IN (@a,@b,@c,@d) OR TypeId IS NULL)` | PR #5285 approach with IS NULL |

5. **Captures** for each execution:
   - Row count returned
   - Index used (secondary IXU vs clustered IXC)
   - Seek vs Scan operation
   - Estimated vs Actual rows
   - Full execution plan XML (viewable in SSMS)

6. **Generates analysis reports**:
   - Correctness check (Q2 row counts must match Q1 baseline)
   - Side-by-side index behavior comparison (IMPROVED / REGRESSED / SAME)
   - Cross-distribution plan stability
   - Summary verdict with seek percentage and degradation risk

### How to Run

1. Open SSMS and connect to a SQL Server 2019+ instance
2. Open `docs/testing/ReferenceResourceTypeId_PerfTest.sql`
3. Execute the entire script (or run Part 1, Part 2, Part 3 separately)
4. Part 1 creates 3 tables and loads ~60M rows total (allow 15-30 minutes)
5. Part 2 runs 24 test executions and stores results in `dbo.TestResults`
6. Part 3 outputs analysis reports — review printed output and query `dbo.TestResults`
7. Click on `ExecutionPlanXml` cells in SSMS to view graphical plans

**Note**: Part 1 is idempotent — if tables already exist with ≥1M rows, data generation is skipped. To force regeneration, drop the tables first.

### Decision Criteria

| Outcome | Decision |
|---------|----------|
| Q2 row counts match Q1 across all scenarios and distributions | ✅ Correctness verified |
| Q2 achieves more index seeks than Q1 | ✅ Performance improvement confirmed |
| Q2 degrades to scans at higher NULL% while Q1 seeks | ⚠️ Approach may be fragile |
| Q2 row counts differ from Q1 (especially S3/S4) | ❌ Correctness bug — NULL handling broken |

---

## Test Results

### Run 1 (2026-02-09) — Uncorrelated Test Data

Initial test data randomly assigned `ReferenceResourceTypeId` values without correlating them to `SearchParamId`. This caused ALL query variants to fail correctness because rows had target types invalid for the search parameter being queried. For example, a `SearchParamId=414` (DiagnosticReport-subject) row might have `ReferenceResourceTypeId=104` (Practitioner), which is not a valid target for that search parameter.

**Root cause**: Test data generation bug, not a real issue with the approach. The data generator was fixed to only assign `ReferenceResourceTypeId` values that are valid FHIR targets for each `SearchParamId`.

Q2-Q7 (which test only 1-2 of the 4 target types) are **expected** to return fewer rows than baseline. The key variants are Q8-Q11, which include ALL target types.

### Run 2 (2026-02-09) — Correlated Test Data, All Target Types (2M rows)

Data regenerated with `ReferenceResourceTypeId` correlated to `SearchParamId` per FHIR R4 spec. Tested 11 query variants including Q8-Q11 with all 4 target types for SearchParamId 414.

**Key findings (Q1 Baseline vs Q10 AllTypes+NULL):**

| Metric | Q1 Baseline | Q10 AllTypes+NULL |
|--------|------------|-------------------|
| Correctness | ✅ PASS | ✅ PASS (matches baseline exactly) |
| Index Seeks (of 12 tests) | 1 (8%) | 6 (50%) |
| S1 degradation at 60% NULL? | N/A | No |

Side-by-side comparison:
- **5 improved** (N/A → Index Seek)
- **5 same** (N/A → N/A)
- **1 regressed** (Index Seek → N/A at 10%/S1 only)
- Q11 (UNION ALL) achieved 7/12 seeks but requires more complex SQL generation

**Conclusion from Run 2**: Q10 (IN + IS NULL) is correct and improves seek rate 6× over baseline. Proceed to Run 3 at 10× scale to confirm results hold.

### Run 3 (pending) — Focused Comparison at 20M Rows

Simplified test: only Q1 (Baseline) vs Q2 (All Types + IS NULL) at 20M rows per table.
This validates whether the Run 2 findings hold at production-like scale.

- **Tables**: 3 × 20M rows = 60M total (10× increase from Run 2)
- **Variants**: Q1-Baseline, Q2-AllTypesNULL only
- **Scenarios**: S1-S4 (unchanged)
- **Distributions**: 10%, 30%, 60% NULL (unchanged)
- **Total tests**: 24 (2 variants × 4 scenarios × 3 distributions)

**Expected**: Q2 matches Q1 row counts exactly and achieves more index seeks.

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
Run perf test (docs/testing/ReferenceResourceTypeId_PerfTest.sql) at 20M rows
    │
    ├─ Q2 row counts match Q1 across all scenarios?
    │     │
    │     ├─ YES → Correctness verified ✅
    │     │     │
    │     │     ├─ Q2 achieves more index seeks than Q1?
    │     │     │     └─ YES → Ship PR #5285 with IS NULL handling
    │     │     │
    │     │     └─ Q2 same or worse seeks than Q1?
    │     │           └─ Consider UNION ALL (Alt 2) or Leave As-Is (Alt 4)
    │     │
    │     └─ NO → Correctness bug — investigate NULL handling
    │
    └─ Q2 degrades at high NULL%?
          │
          ├─ YES → Approach fragile, consider Backfill (Alt 1) or UNION ALL (Alt 2)
          └─ NO → Ship it — plan is stable across distributions
```

## Files

| File | Purpose |
|------|---------|
| [`ReferenceResourceTypeId_PerfTest.sql`](ReferenceResourceTypeId_PerfTest.sql) | SQL script: creates test DB, generates 60M rows (3×20M), runs Q1 vs Q2 comparison, captures results |
| This document | Test plan, alternatives analysis, decision criteria |

## Related Code

| File | Relevance |
|------|-----------|
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/UntypedReferenceRewriter.cs` | PR #5285 changes: adds ReferenceResourceType expressions for multi-target params |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/ReferenceQueryGenerator.cs` | Translates `FieldName.ReferenceResourceType` → `ReferenceResourceTypeId = @val` SQL |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SearchParameterQueryGenerator.cs` | Base class with `VisitMultiary` (handles OR expressions) and `VisitSimpleBinary` |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/ReferenceSearchParamListRowGenerator.cs` | Where NULL vs non-NULL `ReferenceResourceTypeId` is decided during data storage |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/ReferenceSearchParam.sql` | Table and index definitions |
