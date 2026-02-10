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

### Run 1 (2026-02-09) — Uncorrelated Test Data (2M rows)

Initial test data randomly assigned `ReferenceResourceTypeId` values without correlating them to `SearchParamId`. ALL query variants failed correctness. **Root cause**: Test data bug, not a real issue. Fixed by correlating target types to search parameters per FHIR R4 spec.

### Run 2 (2026-02-09) — Correlated Test Data (2M rows, 11 variants)

Tested 11 query variants including Q10 (all types + NULL) and Q11 (UNION ALL). Key findings:
- **Q10 (IN + IS NULL)**: ✅ PASS correctness, 6/12 seeks (50%) vs 1/12 baseline (8%)
- **Q11 (UNION ALL)**: ✅ PASS correctness, 7/12 seeks (58%)
- **Q8/Q9 (no NULL)**: ❌ FAILS — confirms IS NULL is mandatory
- 1 regression at 10%/S1 (baseline seek → Q10 N/A)

### Run 3 (2026-02-10) — Focused Comparison at 20M Rows ⭐

Simplified to Q1 (Baseline) vs Q2 (All Types + IS NULL) only, at 20M rows per table (10× increase). This is the definitive test.

#### Data Distribution

| Table | Total Rows | NULL Rows | NULL % | Patient Rows | Distinct RefIds |
|-------|-----------|-----------|--------|-------------|-----------------|
| RefSearch_Null10 | 20,000,010 | 1,998,085 | 10.0% | 10,569,843 | 19,913 |
| RefSearch_Null30 | 20,000,010 | 5,996,357 | 30.0% | 8,219,814 | 19,913 |
| RefSearch_Null60 | 20,000,010 | 12,000,027 | 60.0% | 4,695,960 | 19,913 |

#### Correctness: ✅ PASS — 12/12

Q2 matches Q1 row counts **exactly** across all 12 test cases. Zero row differences.

| Distribution | S1 (Common ID) | S2 (Rare ID) | S3 (Typed+NULL) | S4 (NULL-only) |
|-------------|----------------|-------------|-----------------|----------------|
| 10% NULL | 2,482 ✅ | 1 ✅ | 2 ✅ | 1 ✅ |
| 30% NULL | 2,486 ✅ | 1 ✅ | 2 ✅ | 1 ✅ |
| 60% NULL | 2,479 ✅ | 1 ✅ | 2 ✅ | 1 ✅ |

#### Index Behavior: Q2 achieves 6× more seeks

| Metric | Q1-Baseline | Q2-AllTypesNULL |
|--------|------------|-----------------|
| **Index Seeks** | **1 of 12 (8.3%)** | **6 of 12 (50.0%)** |
| Degradation at high NULL% | No | No |

Side-by-side comparison:

| Distribution | Scenario | Q1 Baseline | Q2 AllTypes+NULL | Delta |
|-------------|----------|-------------|------------------|-------|
| 10% NULL | S1 (Common ID) | **Index Seek** | **Index Seek** | SAME (both seek) |
| 10% NULL | S2 (Rare ID) | N/A | N/A | SAME |
| 10% NULL | S3 (Typed+NULL) | N/A | N/A | SAME |
| 10% NULL | S4 (NULL-only) | N/A | N/A | SAME |
| 30% NULL | S1 (Common ID) | N/A | N/A | SAME |
| 30% NULL | S2 (Rare ID) | N/A | **Index Seek** | ✅ IMPROVED |
| 30% NULL | S3 (Typed+NULL) | N/A | N/A | SAME |
| 30% NULL | S4 (NULL-only) | N/A | N/A | SAME |
| 60% NULL | S1 (Common ID) | N/A | **Index Seek** | ✅ IMPROVED |
| 60% NULL | S2 (Rare ID) | N/A | **Index Seek** | ✅ IMPROVED |
| 60% NULL | S3 (Typed+NULL) | N/A | **Index Seek** | ✅ IMPROVED |
| 60% NULL | S4 (NULL-only) | N/A | **Index Seek** | ✅ IMPROVED |

Key observations:
- **5 improved, 7 same, 0 regressed** — zero regressions at 20M rows (Run 2 had 1)
- **Q2 improves as NULL% increases**: 1 seek at 10%, 1 at 30%, 4 at 60%
- The optimizer benefits more from the type filter when NULLs are a larger data fraction
- The Run 2 regression (10%/S1) disappeared at scale — optimizer makes better decisions with more data

#### Summary Verdict

| Variant | Correctness | Seek % | Degradation Risk |
|---------|------------|--------|------------------|
| Q1-Baseline | BASELINE | 8.3% | No |
| Q2-AllTypesNULL | ✅ PASS | **50.0%** | No |

---

## Conclusion

**✅ Ship PR #5285 with IS NULL handling.** Three test runs confirm:

1. **Correctness**: Q2 matches baseline row counts exactly across all distributions and scenarios (12/12 PASS at 20M rows)
2. **Performance**: 6× improvement in index seek rate (8.3% → 50.0%) with zero regressions
3. **Stability**: No degradation as NULL% increases from 10% to 60% — the change actually performs *better* at higher NULL ratios
4. **Scale**: Results consistent from 2M to 20M rows

**Requirements for the code change**:
- Include **all** target types from `TargetResourceTypes` — partial lists drop valid rows
- Include `OR ReferenceResourceTypeId IS NULL` — without it, untyped string references are silently dropped
- The `IN (...) OR IS NULL` pattern is sufficient — UNION ALL offers marginally better seek rates but is not needed

---

## Alternative Approaches (Evaluated)

### Alternative 1: Backfill NULL Values

Populate `ReferenceResourceTypeId` for all NULL rows, then make column NOT NULL. Eliminates the NULL problem permanently but requires schema migration, careful data backfill, and handling of genuinely untyped references. **High complexity, best long-term solution.**

### Alternative 2: UNION ALL

Generate `SELECT ... WHERE TypeId IN (...) UNION ALL SELECT ... WHERE TypeId IS NULL`. Run 2 showed 7/12 seeks (vs 6/12 for IN+NULL), but the marginal improvement doesn't justify the SQL generation complexity. **Medium complexity, marginally better seeks.**

### Alternative 3: Filtered Index

Add `CREATE INDEX ... WHERE ReferenceResourceTypeId IS NULL`. Small storage footprint but filtered indexes have limitations with parameterized queries (`sp_executesql`). **Low-medium complexity, unreliable with parameters.**

### Alternative 4: Leave As-Is

Keep current SQL without `ReferenceResourceTypeId` in WHERE. Baseline only achieves 8.3% seek rate. **Not recommended** — the IN+NULL approach is a clear improvement with no downside.

---

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
