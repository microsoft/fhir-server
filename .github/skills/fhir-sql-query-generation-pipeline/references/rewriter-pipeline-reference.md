# Query Rewriter Pipeline Reference

This reference documents every rewriter in the FHIR Server SQL search pipeline, in exact execution order, with what it transforms and why.

## Pipeline Order (Fixed Sequence in SqlServerSearchService)

```csharp
// From SqlServerSearchService.SearchImpl()

Expression searchExpression = sqlSearchOptions.Expression;

// 1. Continuation token AND-ing
if (continuationToken != null) {
    searchExpression = Expression.And(tokenExpression, searchExpression);
}

// 2. Include rewriter (adds IncludeExpression nodes)
SqlRootExpression expression = (SqlRootExpression)searchExpression
    ?.AcceptVisitor(IncludeRewriter.Instance)
    ?? SqlRootExpression.WithResourceTableExpressions();

// 3. Sort rewriter (two-phase sort protocol)
expression = (SqlRootExpression)expression.AcceptVisitor(_sortRewriter, sqlSearchOptions);

// 4. Partition elimination (ResourceTypeId injection)
expression = (SqlRootExpression)expression.AcceptVisitor(_partitionEliminationRewriter);

// 5. Compartment search rewriting
expression = (SqlRootExpression)expression.AcceptVisitor(_compartmentSearchRewriter);
expression = (SqlRootExpression)expression.AcceptVisitor(_smartCompartmentSearchRewriter);

// 6. Chain flattening
expression = (SqlRootExpression)expression.AcceptVisitor(_chainFlatteningRewriter);

// 7. Flattening (nested And collapsing)
expression = (SqlRootExpression)expression.AcceptVisitor(FlatteningRewriter.Instance);

// 8. Not expression rewriting (:not → NotExists)
expression = (SqlRootExpression)expression.AcceptVisitor(NotExpressionRewriter.Instance);

// 9. Top rewriter (adds Top CTE)
expression = (SqlRootExpression)expression.AcceptVisitor(TopRewriter.Instance, searchOptions);

// 10. Search param table expression reordering (selectivity)
expression = (SqlRootExpression)expression.AcceptVisitor(SearchParamTableExpressionReorderer.Instance);

// 11. SqlQueryGenerator emits the actual SQL string
expression.AcceptVisitor(queryGenerator, searchOptions);
```

Note: `SqlRootExpressionRewriter`, `LastUpdatedToResourceSurrogateIdRewriter`, and `DateTimeBoundedRangeRewriter` run earlier in the Core search layer (before `SqlServerSearchService`).

---

## Rewriter 1: SqlRootExpressionRewriter

**When it runs**: Before `SqlServerSearchService`, during initial expression construction.

**What it does**: Partitions the `Expression` tree into:
- `SearchParamTableExpressions` — expressions that map to side tables (Token, String, DateTime, etc.)
- `ResourceTableExpressions` — expressions over `dbo.Resource` columns (`_id`, `_type`, `_lastUpdated`)

**Decision logic**:
```csharp
// TryGetSearchParamTableExpressionQueryGenerator determines table vs resource
var generator = expression.AcceptVisitor(_queryGeneratorFactory);
if (generator != null) {
    // Goes to SearchParamTableExpressions
} else {
    // Goes to ResourceTableExpressions
}
```

**Why it matters**: This is the fundamental split that determines whether a predicate becomes a CTE or a direct WHERE clause on Resource.

---

## Rewriter 2: LastUpdatedToResourceSurrogateIdRewriter

**When it runs**: Before `SqlRootExpressionRewriter`.

**What it does**: Converts `_lastUpdated` predicates to `ResourceSurrogateId` range predicates.

**Transformation examples**:

| FHIR Operator | Input | Output |
|--------------|-------|--------|
| `gt2023-01-01T00:00:00.000Z` | `DateTimeStart > value` | `ResourceSurrogateId >= value+1ms` |
| `gt2023-01-01T00:00:00.123Z` | `DateTimeStart > value` | `ResourceSurrogateId >= ceil(value to ms)+1ms` |
| `ge2023-01-01T00:00:00.000Z` | `DateTimeStart >= value` | `ResourceSurrogateId >= value` |
| `lt2023-01-01T00:00:00.000Z` | `DateTimeEnd < value` | `ResourceSurrogateId < value` |
| `le2023-01-01T00:00:00.123Z` | `DateTimeEnd <= value` | `ResourceSurrogateId < ceil(value to ms)+1ms` |

**Why it matters**: `_lastUpdated` queries become single-column range scans on the clustered index with zero JOINs. This is the schema's most important optimization.

**Precision note**: `ResourceSurrogateId` has millisecond precision (80,000 IDs per ms). Sub-millisecond `_lastUpdated` precision is truncated. The rewriter adds/subtracts 1 millisecond to maintain correct inclusive/exclusive semantics.

---

## Rewriter 3: DateTimeBoundedRangeRewriter

**When it runs**: After table expression combining, before `SqlRootExpressionRewriter`.

**What it does**: Optimizes unbounded DateTime range queries by exploiting `IsLongerThanADay` filtered indexes.

**Problem it solves**: A query like `?date=gt2023-01-01&date=lt2023-12-31` produces `(DateTimeEnd >= 2023-01-01 AND DateTimeStart < 2023-12-31)`. Without bounds, this is an expensive scan.

**Optimization**: The vast majority of DateTime ranges in FHIR are < 1 day. The schema has:
- Filtered index for `IsLongerThanADay = 0` (short ranges)
- Filtered index for `IsLongerThanADay = 1` (long ranges)

**Transformation**:
```
Input:  (And (FieldGreaterThan DateTimeEnd X) (FieldLessThan DateTimeStart Y))
Output: (And 
         (Equals DateTimeIsLongerThanADay true)
         (FieldGreaterThan DateTimeEnd X)
         (FieldLessThan DateTimeStart Y))
```

Then `ConcatenationRewriter` splits this into two CTEs:
1. `IsLongerThanADay = 0` CTE (uses short-range filtered index)
2. `IsLongerThanADay = 1` CTE (uses long-range filtered index)
Combined with `UNION ALL`.

**Why it matters**: Without this, date range queries would scan all `DateTimeSearchParam` rows. The filtered index split reduces I/O by ~95% for typical FHIR data.

---

## Rewriter 4: FlatteningRewriter

**When it runs**: After chain flattening, before Not rewriting.

**What it does**: Flattens nested `And` expressions.

**Transformation**:
```
(And (And a b) (And c d)) → (And a b c d)
(And x) → x
```

**SmartV2 exception**: If `expression.IsSmartV2UnionExpressionForScopesSearchParameters`, it skips flattening to preserve the union structure.

**Why it matters**: Simplifies the expression tree for later rewriters and SQL generation. Reduces visitor recursion depth.

---

## Rewriter 5: ChainFlatteningRewriter

**When it runs**: After compartment rewriters, before flattening.

**What it does**: Flattens `ChainedExpression` nodes into a sequence of `SearchParamTableExpression` entries.

**Example transformation**:
```
Input:  ChainedExpression(
            ResourceTypes=[Observation],
            ReferenceSearchParameter=subject,
            TargetResourceTypes=[Patient],
            Expression=StringExpression(name=Smith))

Output: [
    SearchParamTableExpression(ChainLinkQueryGenerator, SqlChainLinkExpression, Chain, ChainLevel=1),
    SearchParamTableExpression(StringQueryGenerator, StringExpression(name=Smith), Normal, ChainLevel=1)
]
```

**Multi-level chains**: `subject:Patient.general-practitioner:Practitioner.name=Jones` produces:
1. ChainLevel=1: Observation→Patient link
2. ChainLevel=1: Patient name=Smith (filter on target)
3. ChainLevel=2: Patient→Practitioner link
4. ChainLevel=2: Practitioner name=Jones (filter on target)

**Why it matters**: Chains are the most complex SQL pattern. This rewriter breaks them into manageable CTE steps that the generator can emit sequentially.

---

## Rewriter 6: NotExpressionRewriter

**When it runs**: After flattening, before Top rewriting.

**What it does**: Converts `:not` modifiers and `:missing=false` to `NotExists` CTEs.

**Transformation**:
```
Input:  SearchParamTableExpression(TokenQueryGenerator, NotExpression(token=abc))
Output: SearchParamTableExpression(TokenQueryGenerator, token=abc, NotExists)
```

**Seeding**: If the Not expression is the FIRST table expression, a seed `All` expression is prepended:
```
[All (no predicate)] → [NotExists (token=abc)]
```

**Why it matters**: SQL `NOT IN` / `NOT EXISTS` is semantically correct for negated search. Direct `!=` predicates would return resources with OTHER values of the same parameter, which is wrong.

---

## Rewriter 7: IncludeRewriter

**When it runs**: First rewriter in `SearchImpl`.

**What it does**: Processes `_include` and `_revinclude` parameters into `IncludeExpression` nodes.

**Two-phase include protocol**:
1. First phase: Execute filter, get match results, store in `@FilteredData`
2. Second phase: For each include parameter, query `ReferenceSearchParam` JOIN `Resource` using match surrogate IDs as seed

**Why it matters**: Includes are expensive because they JOIN the entire match set against `ReferenceSearchParam`. The `@FilteredData` separation prevents recomputing filters for each include path.

---

## Rewriter 8: SortRewriter

**When it runs**: After include rewriting, before partition elimination.

**What it does**: Implements the two-phase sort protocol for non-`_lastUpdated` sorts.

**Phases**:

| Scenario | Phase 1 | Phase 2 |
|----------|---------|---------|
| Ascending sort, no filter on sort param | Return resources WITH sort value (Sort CTE) | Return resources WITHOUT sort value (NotExists) |
| Descending sort, no filter on sort param | Return resources WITHOUT sort value (NotExists) | Return resources WITH sort value (Sort CTE) |
| Sort param IS in filter | Single-phase SortWithFilter CTE | None |
| Continuation token has sort value | Resume from sort value in Sort CTE | None |
| Continuation token has no sort value | Resume from missing-value set (NotExists) | None |

**SortWithFilter optimization**: If the sort parameter is also a search filter (`name=Smith&_sort=name`), only one phase is needed because all results have the value.

**Why it matters**: FHIR requires correct sort semantics including resources that lack the sort field. Without two-phase, ascending sorts would silently drop resources without values.

---

## Rewriter 9: PartitionEliminationRewriter

**When it runs**: After sort rewriting, before compartment rewriters.

**What it does**: Two critical things:
1. **System-wide search expansion**: Injects `ResourceTypeId IN (all_types)` for searches without `_type` restriction
2. **Continuation token expansion**: Expands `PrimaryKeyValue` into `PrimaryKeyRange` for multi-type continuation

**System-wide search**:
```
Input:  No _type parameter
Output: Adds ResourceTableExpression: ResourceType IN ([Account, ActivityDefinition, ... all 147 types])
```

**Continuation token**:
```
Input:  PrimaryKeyValue(ResourceTypeId=82, ResourceSurrogateId=12345) with > operator
Output: PrimaryKeyRange that excludes type 82 and all lower types from the range
```

**Why it matters**: Without explicit `ResourceTypeId` predicates, SQL Server won't eliminate partitions — it scans all 147 partitions. On a 4TB database this is 40x slower (Issue #3021).

---

## Rewriter 10: SearchParamTableExpressionReorderer

**When it runs**: Last rewriter before SQL generation.

**What it does**: Reorders table expressions by expected selectivity (most selective first).

**Selectivity scores**:

| Expression Type | Score | Rationale |
|----------------|-------|-----------|
| `All` (no predicate) | 20 | Very selective if paired with resource type |
| Reference query | 10 | References are typically selective |
| Compartment query | 10 | Compartments restrict to patient-specific |
| Normal search param | 0 | Baseline |
| NotExpression | -15 | NotExists is expensive (anti-semi-join) |
| Missing parameter | -10 | Missing checks require scanning |
| Include | -20 | Includes are never filter-narrowing |

**Why it matters**: Order of CTEs matters for performance. A highly selective token match first reduces rows for subsequent expensive operations (Missing, NotExists, chains).

---

## Rewriter 11: TopRewriter

**When it runs**: After reordering, just before SQL generation.

**What it does**: Appends a `Top` table expression to cap results.

**SQL output**:
```sql
SELECT DISTINCT TOP (@MaxItemCount + 1) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial
FROM cteLast
ORDER BY Sid1 ASC
```

**+1 semantics**: `MaxItemCount + 1` detects whether more pages exist. If exactly `MaxItemCount` rows returned, no continuation token. If `MaxItemCount + 1`, the extra row indicates more data.

**Why it matters**: Pagination correctness. The extra row is discarded before returning to the client but enables continuation token generation.

---

## SqlQueryGenerator: SQL Emission Details

### CTE Chain Construction

The generator iterates `SearchParamTableExpressions.SortExpressionsByQueryLogic()` and emits:

```sql
;WITH
cte0 AS (
    SELECT T1, Sid1 FROM TokenSearchParam
    WHERE ResourceTypeId = @RT AND SearchParamId = @SPID AND Code = 'abc'
),
cte1 AS (
    SELECT T1, Sid1 FROM StringSearchParam
    WHERE ResourceTypeId = @RT AND SearchParamId = @SPID AND Text = 'def'
    -- Intersection with cte0 implied by JOIN or WHERE EXISTS
),
cte2 AS (
    SELECT DISTINCT TOP (@Max+1) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial
    FROM cte1
    ORDER BY Sid1 ASC
)
```

### Intersection Patterns

Two ways CTEs intersect with predecessors:

**1. JOIN intersection (default for chainLevel > 0 or sort mode)**:
```sql
SELECT ... FROM SearchParamTable
JOIN ctePrev ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1
```

**2. WHERE EXISTS intersection (default for chainLevel = 0)**:
```sql
SELECT ... FROM SearchParamTable
WHERE ResourceTypeId = @RT AND ...
  AND EXISTS (SELECT * FROM ctePrev 
              WHERE T1 = SearchParamTable.ResourceTypeId 
                AND Sid1 = SearchParamTable.ResourceSurrogateId)
```

**When JOIN vs EXISTS is chosen**: Controlled by `UseAppendWithJoin()` — when the predecessor CTE is expected to be small, JOIN is used. For large intermediate results, EXISTS may be more efficient.

### Include Phase SQL

After filter CTEs complete, `@FilteredData` is populated:

```sql
INSERT INTO @FilteredData 
SELECT T1, Sid1, IsMatch, IsPartial, Row 
FROM cteN
/* HASH ... */
OPTION (OPTIMIZE FOR UNKNOWN)
```

Then include CTEs read from `@FilteredData`:
```sql
;WITH cteInclude0 AS (
    SELECT DISTINCT TOP (@IncludeCount+1) 
        refTarget.ResourceTypeId AS T1,
        refTarget.ResourceSurrogateId AS Sid1,
        0 AS IsMatch, 0 AS IsPartial
    FROM ReferenceSearchParam refSource
    JOIN Resource refTarget ON ...
    WHERE refSource.ResourceSurrogateId IN (
        SELECT Sid1 FROM @FilteredData WHERE IsMatch = 1
    )
)
```

### Sort CTE SQL

For non-`_lastUpdated` sorts:
```sql
cteSort AS (
    SELECT 
        sp.ResourceTypeId AS T1,
        sp.ResourceSurrogateId AS Sid1,
        sp.Text AS SortValue        -- or StartDateTime, SingleValue, etc.
    FROM StringSearchParam sp
    JOIN ctePrev ON ...
    WHERE sp.SearchParamId = @SortSPID
      AND sp.IsHistory = 0
)
```

The outer query then:
```sql
ORDER BY SortValue ASC, ResourceTypeId ASC, ResourceSurrogateId ASC
```

### Count-Only Optimization

When `CountOnly = true`:
- Includes are stripped by `RemoveIncludesRewriter`
- Outer query becomes: `SELECT count_big(DISTINCT Sid1) FROM cteLast`
- No Resource table JOIN needed

### Query Hash Comment

```sql
/* HASH 0xA1B2C3D4 */     -- parameter value hash
/* p0p1p2p3 */            -- parameter names in hash
```

This comment is the ONLY difference between queries with different parameter values (when `ReuseQueryPlans = false`). SQL Server treats them as distinct queries for plan cache purposes.
