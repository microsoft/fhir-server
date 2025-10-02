# Complete Transformation: `Patient?name=Smith&birthdate=gt2000-01-01`

## 1. Initial Parsing (HTTP → Core Expression Tree)

**Input**: `GET /Patient?name=Smith&birthdate=gt2000-01-01`

The query parser creates a **Core Expression Tree** (data-store agnostic):

```
MultiaryExpression (AND)
├─ SearchParameterExpression (name)
│  └─ StringExpression
│     ├─ FieldName: String
│     ├─ Value: "Smith"
│     ├─ Operator: StartsWith
│     └─ IgnoreCase: true
│
└─ SearchParameterExpression (birthdate)
   └─ BinaryExpression
      ├─ FieldName: DateTimeStart
      ├─ Operator: GreaterThan
      └─ Value: DateTimeOffset(2000-01-01)
```

## 2. Rewriter Pipeline Transformations

The expression flows through the rewriter pipeline in `SqlServerSearchService.cs:1518-1543`:

### **Stage 1: SqlRootExpressionRewriter**
Converts core expressions to SQL-specific `SqlRootExpression`:

```
SqlRootExpression
├─ SearchParamTableExpressions:
│  ├─ [0] SearchParamTableExpression
│  │   ├─ Kind: Normal
│  │   ├─ QueryGenerator: StringQueryGenerator
│  │   ├─ Predicate: SearchParameterExpression(name)
│  │   └─ Table: StringSearchParam
│  │
│  └─ [1] SearchParamTableExpression
│      ├─ Kind: Normal
│      ├─ QueryGenerator: DateTimeQueryGenerator
│      ├─ Predicate: SearchParameterExpression(birthdate)
│      └─ Table: DateTimeSearchParam
│
└─ ResourceTableExpressions:
    └─ SearchParameterExpression(_type = Patient)
```

### **Stage 2: ChainFlatteningRewriter**
No chains → **No change**

### **Stage 3: SortRewriter**
No explicit `_sort` → Adds default sort by `(_type, _lastUpdated)`:

```
Sort added: [(ResourceType, ASC), (LastUpdated, ASC)]
```

### **Stage 4: PartitionEliminationRewriter**
Since `ResourceType=Patient` is specified, sets partition filter

### **Stage 5: ResourceColumnPredicatePushdownRewriter** ⚡ **CRITICAL**
Analyzes if predicates can move from search parameter tables to the Resource table for massive performance gain.

For this query:
- `name=Smith` → **Cannot pushdown** (requires StringSearchParam table)
- `birthdate=gt2000-01-01` → **Cannot pushdown** (requires DateTimeSearchParam table)

Result: **No pushdown** (stays in search param tables)

### **Stage 6-10: Other Rewriters**
- DateTimeBoundedRangeRewriter
- NumericRangeRewriter
- TopRewriter (adds `TOP 11` for pagination)

**Final Expression Tree After Rewrites**:
```
SqlRootExpression
├─ SearchParamTableExpressions: [StringSearchParam, DateTimeSearchParam]
├─ ResourceTableExpressions: [ResourceType = Patient]
├─ Sort: [(ResourceType, ASC), (LastUpdated, ASC)]
└─ MaxItemCount: 11 (10 + 1 for continuation token detection)
```

## 3. SQL Query Generation

`SqlQueryGenerator.VisitSqlRoot()` orchestrates SQL generation:

### **Phase 1: CTE Generation** (src/...QueryGenerators/SqlQueryGenerator.cs:134-163)

**CTE 0** - String search (via `StringQueryGenerator`):
```sql
WITH cte0 AS (
  SELECT DISTINCT
    ResourceTypeId AS T1,
    ResourceSurrogateId AS Sid1,
    CAST(1 AS bit) AS IsMatch,
    CAST(0 AS bit) AS IsPartial
  FROM dbo.StringSearchParam
  WHERE SearchParamId = @p0  -- ID for 'name' parameter
    AND Text LIKE @p1 + '%' COLLATE Latin1_General_100_CI_AI_SC  -- 'Smith%'
    AND ResourceTypeId = @p2  -- Patient type ID
)
```

**CTE 1** - DateTime search (via `DateTimeQueryGenerator`):
```sql
, cte1 AS (
  SELECT DISTINCT
    ResourceTypeId AS T1,
    ResourceSurrogateId AS Sid1,
    CAST(1 AS bit) AS IsMatch,
    CAST(0 AS bit) AS IsPartial
  FROM dbo.DateTimeSearchParam
  WHERE SearchParamId = @p3  -- ID for 'birthdate' parameter
    AND StartDateTime > @p4  -- 2000-01-01T00:00:00
    AND ResourceTypeId = @p2  -- Patient type ID (reused)
)
```

**CTE 2** - Intersection (AND logic):
```sql
, cte2 AS (
  SELECT cte0.*
  FROM cte0
  WHERE EXISTS (
    SELECT *
    FROM cte1
    WHERE cte1.T1 = cte0.T1
      AND cte1.Sid1 = cte0.Sid1
  )
)
```

> **Why EXISTS?** With 2 search parameters, SqlQueryGenerator uses EXISTS strategy for efficiency. With 6+ parameters, it switches to INNER JOIN strategy.

### **Phase 2: Final SELECT with JOIN** (SqlQueryGenerator.cs:170-299)

```sql
SELECT DISTINCT TOP (@p5)  -- @p5 = 11
  r.ResourceTypeId,
  r.ResourceId,
  r.Version,
  r.IsDeleted,
  r.ResourceSurrogateId,
  r.RequestMethod,
  CAST(1 AS bit) AS IsMatch,
  CAST(0 AS bit) AS IsPartial,
  r.IsRawResourceMetaSet,
  r.SearchParamHash,
  r.RawResource
FROM dbo.Resource r WITH (INDEX(IX_Resource_ResourceTypeId_ResourceSurrgateId))
  JOIN cte2
    ON r.ResourceTypeId = cte2.T1
   AND r.ResourceSurrogateId = cte2.Sid1
WHERE r.ResourceTypeId = @p2  -- Patient
  AND r.IsHistory = 0
  AND r.IsDeleted = 0
ORDER BY
  r.ResourceTypeId ASC,
  r.ResourceSurrogateId ASC
OPTION (OPTIMIZE FOR UNKNOWN)
```

## 4. Complete Generated SQL

```sql
-- Parameter declarations
DECLARE @p0 smallint = 42      -- SearchParamId for 'name'
DECLARE @p1 nvarchar(256) = N'Smith'
DECLARE @p2 smallint = 7       -- ResourceTypeId for Patient
DECLARE @p3 smallint = 19      -- SearchParamId for 'birthdate'
DECLARE @p4 datetime2 = '2000-01-01T00:00:00.0000000'
DECLARE @p5 int = 11

;WITH cte0 AS (
  SELECT DISTINCT
    ResourceTypeId AS T1,
    ResourceSurrogateId AS Sid1,
    CAST(1 AS bit) AS IsMatch,
    CAST(0 AS bit) AS IsPartial
  FROM dbo.StringSearchParam
  WHERE SearchParamId = @p0
    AND Text LIKE @p1 + '%' COLLATE Latin1_General_100_CI_AI_SC
    AND ResourceTypeId = @p2
),
cte1 AS (
  SELECT DISTINCT
    ResourceTypeId AS T1,
    ResourceSurrogateId AS Sid1,
    CAST(1 AS bit) AS IsMatch,
    CAST(0 AS bit) AS IsPartial
  FROM dbo.DateTimeSearchParam
  WHERE SearchParamId = @p3
    AND StartDateTime > @p4
    AND ResourceTypeId = @p2
),
cte2 AS (
  SELECT cte0.*
  FROM cte0
  WHERE EXISTS (
    SELECT *
    FROM cte1
    WHERE cte1.T1 = cte0.T1
      AND cte1.Sid1 = cte0.Sid1
  )
)
SELECT DISTINCT TOP (@p5)
  r.ResourceTypeId,
  r.ResourceId,
  r.Version,
  r.IsDeleted,
  r.ResourceSurrogateId,
  r.RequestMethod,
  CAST(1 AS bit) AS IsMatch,
  CAST(0 AS bit) AS IsPartial,
  r.IsRawResourceMetaSet,
  r.SearchParamHash,
  r.RawResource
FROM dbo.Resource r WITH (INDEX(IX_Resource_ResourceTypeId_ResourceSurrgateId))
  JOIN cte2
    ON r.ResourceTypeId = cte2.T1
   AND r.ResourceSurrogateId = cte2.Sid1
WHERE r.ResourceTypeId = @p2
  AND r.IsHistory = 0
  AND r.IsDeleted = 0
ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC
OPTION (OPTIMIZE FOR UNKNOWN);
```

## Key Architectural Decisions

1. **CTE Strategy**: Each search parameter gets its own CTE for modularity and plan reuse
2. **EXISTS vs JOIN**: With <6 parameters, uses EXISTS for intersection; ≥6 uses INNER JOIN
3. **Parameter Hashing**: Query structure hash enables plan reuse across different parameter values
4. **Index Hint**: Forces optimal index when searching single resource type
5. **Partition Pruning**: ResourceTypeId filtering enables partition elimination on partitioned tables
6. **OPTIMIZE FOR UNKNOWN**: Prevents parameter sniffing issues with cached plans

## Performance Characteristics

- **Best case**: Both indexes on `(ResourceTypeId, SearchParamId, Text)` and `(ResourceTypeId, SearchParamId, StartDateTime)` → Index seeks
- **Intersection cost**: EXISTS on surrogate IDs (highly selective)
- **Final JOIN**: Nested loop join on primary key (ResourceTypeId, ResourceSurrogateId)

This architecture allows the FHIR server to translate arbitrary FHIR search queries into efficient, parameterized T-SQL with plan reuse.

## Flow Diagram

```
HTTP Request
    ↓
┌───────────────────────────────────────────────────────────────────┐
│ 1. PARSING PHASE                                                  │
│    ExpressionParser.Parse()                                       │
│    → Creates Core Expression Tree (data-store agnostic)           │
└───────────────────────────────────────────────────────────────────┘
    ↓
┌───────────────────────────────────────────────────────────────────┐
│ 2. REWRITER PIPELINE (SqlServerSearchService.cs:1518-1543)        │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 1. SqlRootExpressionRewriter                            │   │
│    │    → Converts to SqlRootExpression                      │   │
│    │    → Assigns QueryGenerators to each search param       │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 2. ChainFlatteningRewriter                              │   │
│    │    → Flattens chained references (none in this query)   │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 3. SortRewriter                                          │   │
│    │    → Adds default sort: (ResourceType, LastUpdated)     │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 4. PartitionEliminationRewriter                         │   │
│    │    → Sets ResourceTypeId filter for partition pruning   │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 5. ResourceColumnPredicatePushdownRewriter ⚡           │   │
│    │    → Analyzes if predicates can move to Resource table  │   │
│    │    → HUGE performance gain when applicable              │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ 6-10. Other Rewriters                                    │   │
│    │    → DateTimeBoundedRangeRewriter                       │   │
│    │    → NumericRangeRewriter                               │   │
│    │    → TopRewriter                                         │   │
│    └─────────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────────┘
    ↓
┌───────────────────────────────────────────────────────────────────┐
│ 3. QUERY GENERATION (SqlQueryGenerator.VisitSqlRoot)              │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ Phase 1: CTE Generation                                  │   │
│    │  • StringQueryGenerator → cte0 (StringSearchParam)      │   │
│    │  • DateTimeQueryGenerator → cte1 (DateTimeSearchParam)  │   │
│    │  • Intersection logic → cte2 (EXISTS/JOIN)              │   │
│    └─────────────────────────────────────────────────────────┘   │
│    ┌─────────────────────────────────────────────────────────┐   │
│    │ Phase 2: Final SELECT                                    │   │
│    │  • JOIN Resource table with final CTE                   │   │
│    │  • Apply WHERE clauses (IsHistory, IsDeleted)           │   │
│    │  • Add ORDER BY (primary key sort)                      │   │
│    │  • Add OPTION (OPTIMIZE FOR UNKNOWN)                    │   │
│    └─────────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────────┘
    ↓
┌───────────────────────────────────────────────────────────────────┐
│ 4. EXECUTION (SqlServerFhirDataStore)                             │
│    → Execute parameterized T-SQL                                  │
│    → Query plan cached based on structure hash                    │
│    → Return SearchResult with ResourceWrapper[]                   │
└───────────────────────────────────────────────────────────────────┘
    ↓
SearchResult → HTTP Response (JSON Bundle)
```

## Key File References

- **Expression Tree Base**: `Core/Features/Search/Expressions/Expression.cs`
- **SQL Root**: `SqlServer/Features/Search/Expressions/SqlRootExpression.cs`
- **Orchestration**: `SqlServer/Features/Search/SqlServerSearchService.cs:1518-1543`
- **Query Generator**: `SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SqlQueryGenerator.cs`
- **String Generator**: `SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/StringQueryGenerator.cs`
- **DateTime Generator**: `SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/DateTimeQueryGenerator.cs`
