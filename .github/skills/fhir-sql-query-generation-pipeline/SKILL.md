---
name: fhir-sql-query-generation-pipeline
description: |
  Expert knowledge of the Microsoft FHIR Server C# search-to-SQL query generation pipeline.
  Construction-oriented: rewriter order, expression-tree shape, why SQL looks the way it does.
  For slow-query triage, design-limitation vs bug, index/hint recommendations, and Query Store playbooks, load `fhir-sql-diagnose-query-perf` alongside this skill.
  Activate when: analyzing how FHIR search queries translate to SQL, debugging generated SQL,
  understanding CTE structures from search expressions, working with expression rewriters,
  SqlServerSearchService, SqlQueryGenerator, search parameter query generators, chain searches,
  _include/_revinclude SQL generation, _sort SQL generation, continuation tokens, query plan hashing,
  "SearchParamTableExpression", "SqlRootExpression", "expression rewriter", "query generator",
  "CTE", "chain search", "include search", FHIR search SQL, "CustomQueries", "query cache",
  "HashingSqlQueryParameterManager", "SqlCommandSimplifier", long-running search queries,
  search query performance, "OPTION (RECOMPILE)", "OPTION (OPTIMIZE FOR UNKNOWN)".
---

# FHIR SQL Query Generation Pipeline

## Architecture Overview

FHIR search queries traverse a multi-stage expression-tree pipeline before becoming SQL. Understanding this pipeline is essential for diagnosing incorrect or slow SQL.

## Companion skill: load alongside

When the question is *"this generated SQL is slow / timing out / picking a bad plan / has a missing index"*, load **`fhir-sql-diagnose-query-perf`** as well. It owns slow-query triage (Categories A–F: missing partition elimination, system-wide search, include join explosion, chain depth, sort on low-selectivity param, count complexity, token text), the design-limitation-vs-bug checklist, optimizer-hint guidance (`@DummyTop`, `OPTIMIZE FOR UNKNOWN`, `MAXDOP 1`, `OPTION (RECOMPILE)`), and Query Store playbooks. Use *this* skill to understand *how* the SQL was built; use the diag skill to decide *what to do* about a slow plan.

### Pipeline Stages (in order)

1. **FHIR search string parsed** → `Expression` tree (Core layer, not SQL-specific)
2. **`SqlRootExpressionRewriter`** — partitions expressions into `SearchParamTableExpressions` (search param side tables) vs `ResourceTableExpressions` (dbo.Resource predicates like `_id`, `_type`, `_lastUpdated`)
3. **`FlatteningRewriter`** — flattens nested `And` expressions: `(And (And a b) (And c d))` → `(And a b c d)`. Skips SmartV2 union expressions.
4. **`LastUpdatedToResourceSurrogateIdRewriter`** — converts `_lastUpdated` predicates to `ResourceSurrogateId` range predicates. Uses millisecond truncation with tick adjustment: `gt` becomes `>= (truncated + 1ms)`.
5. **`DateTimeBoundedRangeRewriter`** — optimizes unbounded DateTime ranges by adding `IsLongerThanADay` filtered index exploitation.
6. **`ChainFlatteningRewriter`** — flattens chained expressions (`_has`, `subject:Patient.name`) into sequential `SearchParamTableExpression` entries with `ChainLevel` tracking. For each chained expression it emits a `Chain` CTE (the reference traversal) followed by a `Normal` CTE (the target-resource filter). **Important**: the `Normal` CTE is only emitted when `queryGenerator != null`. If the chained expression's target predicate resolves to a `null` generator (i.e., it's a resource-table column like `_id`, `_type`, `_lastUpdated`), the predicate is instead embedded directly in `expressionOnTarget` on the Chain link expression — this is intentional and correct for those parameters. If both `queryGenerator == null` AND the expression is not a known resource-table predicate AND not a nested chain, the filter is **silently dropped** — this is the rare edge case to watch for when debugging chain queries that return too many results. *When this stage produces slow plans (multi-level nested loops on deep chains), see `fhir-sql-diagnose-query-perf` → Category C.*
7. **`NotExpressionRewriter`** — converts `:not` modifiers to `NotExists` table expressions.
8. **`IncludeRewriter`** — processes `_include` and `_revinclude` into `Include` table expressions. *When include-join explosion makes a query slow, see `fhir-sql-diagnose-query-perf` → Category B.*
9. **`SortRewriter`** — handles non-`_lastUpdated` sorts via two-phase search (resources-with-value first, resources-without-value second). *When sort dominates query cost on large resource types, see `fhir-sql-diagnose-query-perf` → Category D for the `DateTimeSearchParam` index gap and `INCLUDE ResourceSurrogateId` / DESC-index recommendations.*
10. **`PartitionEliminationRewriter`** — injects `ResourceTypeId IN (...)` for system-wide searches and expands continuation tokens into `PrimaryKeyRange`. *Distinguishing a true partition-elimination bug from the inherent system-wide-search design limitation: see `fhir-sql-diagnose-query-perf` → Category A vs A2.*
11. **`SearchParamTableExpressionReorderer`** — reorders by selectivity: Reference/Compartment (10) > normal (0) > NotExists (-15) > Missing (-10) > Include (-20).
12. **`TopRewriter`** — appends a `Top` table expression for pagination.
13. **`SqlQueryGenerator`** — visitor that emits the actual SQL string with CTEs, JOINs, `@FilteredData` table variable.

## Critical Design Decisions (WHY)

### Why CTEs instead of subqueries?
The SQL is built as a chain of CTEs (`cte0`, `cte1`, ...) where each CTE filters the previous result set. This enables:
- Incremental result narrowing
- Sort value propagation through JOINs
- Clean separation of union/intersect logic
- Table variable `@FilteredData` persistence before `_include` processing

### Why `@FilteredData` table variable?
Before `_include`/`_revinclude` processing, the filter results are persisted into `@FilteredData` (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int, optional SortValue). This separates the expensive filter phase from the include phase, avoiding recomputation.

### Why the two-phase sort protocol?
FHIR resources may not have values for the sort field. To maintain correct FHIR sort semantics:
- **Ascending sort, phase 1**: Return resources WITH sort values (via `Sort` CTE). If result count < MaxItemCount, phase 2 searches resources WITHOUT the value via `NotExists`.
- **Descending sort, phase 1**: Return resources WITHOUT sort values first. Phase 2 searches resources WITH values.
- The continuation token carries a sentinel (`SqlSearchConstants.SortSentinelValueForCt`) to coordinate phases.

### Why query parameter hashing?
`HashingSqlQueryParameterManager` computes a hash of parameter VALUES (not TOP/continuation parameters) and injects it as a SQL comment `/* HASH ... */`. This prevents query plan reuse across different parameter values, avoiding parameter sniffing issues. Controlled by `Search.ReuseQueryPlans.IsEnabled` parameter.

### Why `OPTION (OPTIMIZE FOR UNKNOWN)`?
In `SqlQueryGenerator.AddOptionClause()`: when a query has >1 search parameter, one is `identifier`, and there is an `_include`, the optimizer makes poor cardinality estimates. `OPTIMIZE FOR UNKNOWN` forces generic plans based on statistics rather than parameter values.

## Key C# Types and Their SQL Output

### SqlRootExpression Structure
```csharp
// SearchParamTableExpressions: CTE chain over side tables
// ResourceTableExpressions: direct predicates on dbo.Resource
```

### SearchParamTableExpression Kinds and SQL Patterns

| Kind | SQL Pattern | Purpose |
|------|-------------|---------|
| `Normal` | `SELECT T1, Sid1 FROM TokenSearchParam WHERE ...` | Standard search param filter |
| `Concatenation` | `SELECT * FROM cteN UNION ALL SELECT T1, Sid1 FROM ...` | DateTime range split (short + long) |
| `All` | `SELECT T1, Sid1 FROM dbo.Resource WHERE ...` | Seed all resources (for `:not` or no-params) |
| `NotExists` | `SELECT T1, Sid1 FROM cteN WHERE Sid1 NOT IN (SELECT ...)` | `:not` modifier, `:missing=false` |
| `Top` | `SELECT DISTINCT TOP(@MaxItemCount+1) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial` | Pagination cap |
| `Chain` | `SELECT T1, Sid1, T2, Sid2 FROM ReferenceSearchParam JOIN Resource ...` | `_has`, chained references |
| `Include` | `SELECT DISTINCT TOP(@IncludeCount+1) T1, Sid1, 0 AS IsMatch ...` | `_include`/`_revinclude` |
| `IncludeLimit` | Limits included resources per type | Per-type include capping |
| `Sort` | `SELECT T1, Sid1, SortValue FROM ...` | Non-`_lastUpdated` sort |
| `SortWithFilter` | Same but intersected with filter | Sort parameter also in WHERE |
| `Union` | `SELECT T1, Sid1 FROM Resource WHERE ResourceTypeId=X ...` | Smart compartment scopes |

## Search-to-SQL Translation by Parameter Type

### Token Search (`code=system|value`)
- Table: `TokenSearchParam`
- System lookup: `TryGetSystemId` → `SystemId` int. If unknown, emits scalar subquery against `dbo.System`.
- Code handling:
  - Length < 256: `Code = 'value'` only
  - Length == 256: `Code = 'value' AND CodeOverflow IS NULL`
  - Length > 256: `Code = LEFT(value,256)' AND CodeOverflow IS NOT NULL AND CodeOverflow = remainder`
- Missing field: `TokenSystem` → `SystemId IS NULL` check

### String Search (`name=Smith`)
- Table: `StringSearchParam`
- Column: `Text` (nvarchar(256), CI_AI_SC collation)
- Overflow: `TextOverflow` (nvarchar(max)) for values > 256 chars
- StringExpression operators: `StartsWith`, `Equals`, `Contains` — all case-insensitive

### DateTime Search (`date=gt2023-01-01`)
- Table: `DateTimeSearchParam`
- Columns: `StartDateTime`, `EndDateTime` (datetime2)
- `IsLongerThanADay` bit for filtered index exploitation
- Binary operators on `DateTimeStart`/`DateTimeEnd` with inclusive/exclusive handling
- `eq` rewritten as range `(StartDateTime <= value AND EndDateTime >= value)` before SQL generation

### Number/Quantity Search
- Table: `NumberSearchParam` / `QuantitySearchParam`
- Point values: `SingleValue` (nullable decimal)
- Ranges: `LowValue`/`HighValue` (non-nullable decimal)
- Quantity adds `QuantityCodeId` and `SystemId` lookups
- Missing field uses `SingleValue IS NULL` for point-value existence

### Reference Search (`subject=Patient/123`)
- Table: `ReferenceSearchParam`
- Columns: `BaseUri`, `ReferenceResourceTypeId`, `ReferenceResourceId`
- Resource type resolved via `TryGetResourceTypeId` → short. Unknown types emit `0 = 1` (no results).
- Chain searches use `ReferenceSearchParam` JOIN `Resource` on `ReferenceResourceTypeId = ResourceTypeId AND ReferenceResourceId = ResourceId`

## Chain Search SQL Structure

```sql
-- ChainLevel=1: Links reference source to target resource
SELECT 
    refSource.ResourceTypeId AS T1,
    refSource.ResourceSurrogateId AS Sid1,
    refTarget.ResourceTypeId AS T2,
    refTarget.ResourceSurrogateId AS Sid2
FROM ReferenceSearchParam refSource
JOIN Resource refTarget 
    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
    AND refSource.ReferenceResourceId = refTarget.ResourceId
WHERE refSource.SearchParamId = @SearchParamId
  AND refSource.ResourceTypeId IN (@SourceTypeIds)
  AND refSource.ReferenceResourceTypeId IN (@TargetTypeIds)
  AND refTarget.ResourceTypeId = ctePrev.T2  -- intersection with predecessor
  AND refTarget.ResourceSurrogateId = ctePrev.Sid2
```

For reverse chains (`_has`), T1/T2 and Sid1/Sid2 are swapped.

## Continuation Token Mechanics

- **Primary key continuation**: Encodes `(ResourceTypeId, ResourceSurrogateId)`. The `PartitionEliminationRewriter` expands this into a `PrimaryKeyRange` that respects sort direction and type restrictions.
- **Sort continuation**: Includes `SortValue` for non-`_lastUpdated` sorts. Phase transitions use sentinel values.
- **Include continuation**: `IncludesContinuationToken` tracks match boundaries and include progress across multiple SQL round-trips.

## Query Plan Reuse and Cache Control

- `Search.ReuseQueryPlans.IsEnabled` parameter (default: disabled/0) controls whether query parameter hashing is skipped for plan reuse.
- When enabled, identical parameter values reuse plans from query cache.
- When disabled (default), each distinct parameter value gets its own plan via hash comment differentiation.
- `_queryCaching` header can override: `disabled`, `enabled`, `both` (runs both, returns first).

## Common Agent Mistakes to AVOID

1. **Recommending `FORCE ORDER` or plan guides** — The `@DummyTop + OPTIMIZE FOR` and hash-comment patterns already address plan stability. Adding `FORCE ORDER` breaks the CTE optimizer's ability to reorder joins.

2. **Suggesting `WHERE IsHistory = 0` filtered indexes on search-param tables** — Most modern search-param tables (`TokenSearchParam`, `StringSearchParam`, `DateTimeSearchParam`, `NumberSearchParam`, `QuantitySearchParam`, `ReferenceSearchParam`, `UriSearchParam`, all composites) do **not** have an `IsHistory` column at all; rows for historical versions are deleted by `MergeResources` rather than flagged. Only `dbo.TokenText` and `dbo.Resource` itself still carry `IsHistory`. Filtered indexes on search-param tables today filter by sparse value columns (e.g., `WHERE TextOverflow IS NOT NULL`, `WHERE SingleValue IS NULL`), not by history.

3. **Proposing to merge CTEs into subqueries** — The CTE chain is intentional for incremental filtering and sort propagation. Flattening would break sort handling and include logic.

4. **Recommending `DISTINCT` removal** — `DISTINCT` in the outer SELECT is required because `_include` can return the same resource through multiple include paths.

5. **Suggesting parameter sniffing fixes with `OPTION (RECOMPILE)` everywhere** — The codebase already uses targeted `OPTION (RECOMPILE)` only for SmartV2 include regeneration. Broad `RECOMPILE` would destroy plan cache benefits.

6. **Proposing to replace `ResourceSurrogateId` range scans with datetime column comparisons** — `_lastUpdated` is intentionally encoded into `ResourceSurrogateId` to avoid JOINs. Reverting to datetime comparisons would defeat the schema's keystone optimization.

7. **Recommending to remove the `TOP (@DummyTop) ... OPTION (OPTIMIZE FOR (@DummyTop = 1))` pattern** — This is a plan stability mechanism. Removing it causes the optimizer to choose hash joins/scans based on actual (large) cardinality estimates.

8. **Diagnosing chain searches that return too many results**: If a chained search like `subject:Patient.name=Smith` returns all patients instead of only those with a matching practitioner named Smith, verify:
   - The `Chain` CTE carries `T1, Sid1, T2, Sid2` (not just `ResourceSurrogateId`)
   - The `Normal` CTE for the target filter exists at `ChainLevel = 1` (look for a `StringSearchParam` or equivalent join in the CTE after the chain link)
   - The search parameter for the chained field is registered and its `ColumnLocation()` does NOT have `ResourceTable` flag set — if it does, `queryGenerator` returns null and the filter is embedded in `expressionOnTarget` on the chain link instead of a separate CTE (this is correct for `_id`, `_type`)

When asked about a specific rewriter's behavior, ALWAYS verify against actual source code:

1. **Find the actual .cs file** — Use the file paths in the "Files to Reference" section below
2. **Read the `AcceptVisitor`/`Visit` method** — Quote actual C# logic, not skill summaries
3. **Cite line numbers** — Be specific about where key logic occurs
4. **Find corresponding test files** — Look for `*Tests.cs` files in the test project
5. **Prefer code evidence over abstractions** — Lead with what the ACTUAL CODE does

**Example of strong evidence:**
> In `DateTimeBoundedRangeRewriter.cs`, `VisitSqlRoot` iterates `expression.SearchParamTableExpressions` and rewrites DateTime ranges by adding `Equals(SqlFieldName.DateTimeIsLongerThanADay, ...)` expressions. The `ConcatenationRewriter` base class then splits these into two `SearchParamTableExpression` entries: `Normal` (short ranges) and `Concatenation` (long ranges). In `SqlQueryGenerator.cs`, the `Concatenation` kind emits `UNION ALL` between the predecessor CTE and the current table expression.

**Example of weak evidence:**
> `DateTimeBoundedRangeRewriter` optimizes unbounded DateTime ranges using filtered indexes.

## Files to Reference

| File | Purpose |
|------|---------|
| `SqlServerSearchService.cs` | Orchestrates search, runs rewriter pipeline, executes SQL |
| `SqlQueryGenerator.cs` | Main SQL string emitter (CTE builder) |
| `SqlRootExpressionRewriter.cs` | Partitions expressions into table vs resource predicates |
| `PartitionEliminationRewriter.cs` | Injects ResourceTypeId for partition elimination |
| `SortRewriter.cs` | Two-phase sort protocol |
| `ChainFlatteningRewriter.cs` | Flattens chained searches into CTE sequence |
| `LastUpdatedToResourceSurrogateIdRewriter.cs` | `_lastUpdated` → `ResourceSurrogateId` |
| `DateTimeBoundedRangeRewriter.cs` | `IsLongerThanADay` optimization |
| `NotExpressionRewriter.cs` | `:not` → `NotExists` |
| `SearchParamTableExpressionReorderer.cs` | Selectivity-based reordering |
| `SearchParamTableExpressionQueryGeneratorFactory.cs` | Maps search param types to generators |
| `CustomQueries.cs` | Query hash-to-custom-SQL override mechanism |
