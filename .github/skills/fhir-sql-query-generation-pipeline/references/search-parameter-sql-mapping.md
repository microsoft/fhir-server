# Search Parameter Type to SQL Mapping Reference

This reference documents the exact SQL generated for each FHIR search parameter type. Use this when debugging generated SQL, verifying index usage, or explaining why a particular search query produces the SQL shape it does.

## Token Search Parameters

### Tables
- **Primary**: `dbo.TokenSearchParam`
- **Text search**: `dbo.TokenText` (for `:text` modifier)

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
SystemId            int      NULL        -- FK to dbo.System
Code                varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
CodeOverflow        varchar(max) NULL
```

### SQL Patterns

**Exact token match** (`code=http://loinc.org|8302-2`):
```sql
SELECT ResourceTypeId, ResourceSurrogateId 
FROM dbo.TokenSearchParam
WHERE ResourceTypeId = @RT
  AND SearchParamId = @SPID
  AND SystemId = @SYSID        -- resolved via TryGetSystemId
  AND Code = '8302-2'
  AND IsHistory = 0
```

**Token without system** (`code=8302-2`):
```sql
-- SystemId IS NULL means "no system specified"
WHERE SearchParamId = @SPID AND Code = '8302-2'
```

**Token with overflow** (code > 256 chars):
```sql
WHERE Code = 'first-256-chars...'
  AND CodeOverflow IS NOT NULL
  AND CodeOverflow = 'remaining-chars...'
```

**Token text modifier** (`code:text=weight`):
```sql
SELECT ResourceTypeId, ResourceSurrogateId
FROM dbo.TokenText
WHERE ResourceTypeId = @RT
  AND SearchParamId = @SPID
  AND Text LIKE '%weight%'        -- CI_AI collation, always leading-wildcard-capable
  AND IsHistory = 0
```

**Token missing** (`code:missing=true`):
```sql
-- NotExists pattern
SELECT T1, Sid1 FROM ctePrev
WHERE Sid1 NOT IN (
    SELECT ResourceSurrogateId 
    FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @RT AND SearchParamId = @SPID
)
```

### Index Usage
- `IX_TokenSearchParam_SearchParamId_Code_SystemId` — seeks for exact matches
- `IX_TokenSearchParam_SearchParamId_Code` — seeks when system not specified
- Filtered: `WHERE IsHistory = 0`

---

## String Search Parameters

### Tables
- **Primary**: `dbo.StringSearchParam`

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
Text                nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
TextOverflow        nvarchar(max) NULL
```

### SQL Patterns

**Exact string** (`name=Smith`):
```sql
WHERE SearchParamId = @SPID AND Text = 'Smith'
```

**Starts with** (`name=Sm*` → `StartsWith`):
```sql
WHERE SearchParamId = @SPID AND Text LIKE 'Sm%'
```

**Contains** (`name:contains=ith`):
```sql
WHERE SearchParamId = @SPID AND Text LIKE '%ith%'
```

**String with overflow** (text > 256 chars):
```sql
WHERE Text = 'first-256...'
  AND TextOverflow IS NOT NULL
  AND TextOverflow LIKE '%remaining...%'
```

### Collation Impact
`Latin1_General_100_CI_AI_SC` means:
- Case-insensitive: `Smith` = `smith` = `SMITH`
- Accent-insensitive: `resume` = `résumé`
- Supplementary character aware: handles emoji, extended Unicode

---

## DateTime Search Parameters

### Tables
- **Primary**: `dbo.DateTimeSearchParam`

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
StartDateTime       datetime2 NOT NULL
EndDateTime         datetime2 NOT NULL
IsLongerThanADay    bit      NOT NULL
```

### SQL Patterns

**Exact date** (`date=2023-01-01`):
```sql
-- Rewritten as range before SQL generation
WHERE SearchParamId = @SPID
  AND StartDateTime <= '2023-01-01T23:59:59.9999999'
  AND EndDateTime >= '2023-01-01T00:00:00'
```

**Greater than** (`date=gt2023-01-01`):
```sql
WHERE SearchParamId = @SPID AND EndDateTime >= '2023-01-01T00:00:00'
```

**Less than** (`date=lt2023-01-01`):
```sql
WHERE SearchParamId = @SPID AND StartDateTime < '2023-01-01T00:00:00'
```

**Bounded range** (`date=gt2023-01-01&date=lt2023-12-31`):
```sql
-- DateTimeBoundedRangeRewriter optimizes this
-- Case 1: Short ranges (IsLongerThanADay = 0) use filtered index
WHERE IsLongerThanADay = 0
  AND SearchParamId = @SPID
  AND EndDateTime >= '2023-01-01'
  AND StartDateTime < '2023-12-31'

-- Case 2: Long ranges (IsLongerThanADay = 1) via concatenation CTE
-- Uses UNION ALL between short and long filtered index scans
```

### Index Usage
- `IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime` — for range queries
- `IX_DateTimeSearchParam_SearchParamId_IsLongerThanADay_Start_End` — filtered for long ranges
- Both filtered: `WHERE IsHistory = 0`

---

## Number Search Parameters

### Tables
- **Primary**: `dbo.NumberSearchParam`

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
SingleValue         decimal(18,6) NULL        -- for point values
LowValue            decimal(18,6) NOT NULL    -- for ranges
HighValue           decimal(18,6) NOT NULL    -- for ranges
```

### SQL Patterns

**Point value** (`value=100`):
```sql
WHERE SearchParamId = @SPID
  AND SingleValue IS NOT NULL
  AND SingleValue = 100
```

**Range query** (`value=gt50&value=lt200`):
```sql
WHERE SearchParamId = @SPID
  AND SingleValue IS NULL          -- indicates this is a range-valued row
  AND LowValue < 200
  AND HighValue > 50
```

**Approximately** (`value=ap100`):
```sql
-- Treated as range: [90, 110] with 10% default tolerance
WHERE SingleValue IS NULL
  AND LowValue < 110
  AND HighValue > 90
```

### Index Usage
Two complementary filtered indexes:
- `IX_NumberSearchParam_SearchParamId_SingleValue` WHERE `IsHistory = 0 AND SingleValue IS NOT NULL`
- `IX_NumberSearchParam_SearchParamId_LowValue_HighValue` WHERE `IsHistory = 0 AND SingleValue IS NULL`

---

## Quantity Search Parameters

### Tables
- **Primary**: `dbo.QuantitySearchParam`
- **Lookup**: `dbo.QuantityCode` (quantity code → id)

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
SystemId            int      NULL
QuantityCodeId      int      NULL
SingleValue         decimal(18,6) NULL
LowValue            decimal(18,6) NOT NULL
HighValue           decimal(18,6) NOT NULL
```

### SQL Patterns

**Quantity with system+code** (`value-quantity=http://unitsofmeasure.org|kg|70`):
```sql
WHERE SearchParamId = @SPID
  AND SystemId = @SYSID
  AND QuantityCodeId = @QCODEID   -- resolved via TryGetQuantityCodeId
  AND SingleValue = 70
```

**Quantity code lookup failure**:
```sql
-- If system or code not found in lookup tables:
-- Emits scalar subquery against dbo.System / dbo.QuantityCode
SystemId = (SELECT SystemId FROM dbo.System WHERE Value = 'http://...')
```

---

## Reference Search Parameters

### Tables
- **Primary**: `dbo.ReferenceSearchParam`

### Columns
```sql
ResourceTypeId         smallint NOT NULL
ResourceSurrogateId    bigint   NOT NULL
SearchParamId          smallint NOT NULL
IsHistory              bit      NOT NULL
BaseUri                varchar(256) NULL
ReferenceResourceTypeId smallint NOT NULL
ReferenceResourceId     varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
```

### SQL Patterns

**Exact reference** (`subject=Patient/123`):
```sql
WHERE SearchParamId = @SPID
  AND ReferenceResourceTypeId = 82   -- Patient
  AND ReferenceResourceId = '123'
```

**Reference by type only** (`subject:Patient=123`):
```sql
-- Same SQL, type resolved at parse time
WHERE ReferenceResourceTypeId = @RTID AND ReferenceResourceId = '123'
```

**Chained reference** (`subject:Patient.name=Smith`):
```sql
-- Chain CTE: ReferenceSearchParam JOIN Resource
SELECT 
    refSource.ResourceTypeId AS T1,
    refSource.ResourceSurrogateId AS Sid1,
    refTarget.ResourceTypeId AS T2,
    refTarget.ResourceSurrogateId AS Sid2
FROM dbo.ReferenceSearchParam refSource
JOIN dbo.Resource refTarget
    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
    AND refSource.ReferenceResourceId = refTarget.ResourceId
WHERE refSource.SearchParamId = @SPID
  AND refSource.ResourceTypeId IN (@SourceTypeIds)
  AND refSource.ReferenceResourceTypeId IN (@TargetTypeIds)
  AND refTarget.ResourceTypeId = ctePrev.T2
  AND refTarget.ResourceSurrogateId = ctePrev.Sid2
```

### Index Usage
- `IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId` — primary access path
- Filtered: `WHERE IsHistory = 0`

---

## URI Search Parameters

### Tables
- **Primary**: `dbo.UriSearchParam`

### Columns
```sql
ResourceTypeId      smallint NOT NULL
ResourceSurrogateId bigint   NOT NULL
SearchParamId       smallint NOT NULL
IsHistory           bit      NOT NULL
Uri                 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
```

### SQL Patterns

**Exact URI** (`url=http://example.org`):
```sql
WHERE SearchParamId = @SPID AND Uri = 'http://example.org'
```

**Below modifier** (`url:below=http://example.org/path`):
```sql
WHERE SearchParamId = @SPID AND Uri LIKE 'http://example.org/path%'
```

---

## Composite Search Parameters

Composite parameters span multiple tables. Examples:

### Token+Token Composite (`code-value-concept`)
Uses `dbo.TokenTokenCompositeSearchParam`:
```sql
WHERE SearchParamId = @SPID
  AND Code1 = 'value1' AND SystemId1 = @SYSID1
  AND Code2 = 'value2' AND SystemId2 = @SYSID2
```

### Token+String Composite (`code-value-string`)
Uses `dbo.TokenStringCompositeSearchParam`:
```sql
WHERE SearchParamId = @SPID
  AND Code1 = 'code' AND SystemId1 = @SYSID
  AND Text2 LIKE 'string%'
```

### Token+DateTime Composite
Uses `dbo.TokenDateTimeCompositeSearchParam` with StartDateTime2/EndDateTime2 columns.

### Token+Quantity Composite
Uses `dbo.TokenQuantityCompositeSearchParam` with QuantityCodeId2, SingleValue2, etc.

---

## Resource Table Predicates

Some parameters are resolved directly against `dbo.Resource`:

### `_id` (Resource ID)
```sql
-- ResourceTableSearchParameterQueryGenerator
WHERE ResourceTypeId = @RT AND ResourceId = 'patient-id'
```

### `_type`
```sql
WHERE ResourceTypeId IN (@TypeIds)
```

### `_lastUpdated`
```sql
-- Rewritten to ResourceSurrogateId by LastUpdatedToResourceSurrogateIdRewriter
WHERE ResourceSurrogateId >= @StartSurrogateId
  AND ResourceSurrogateId < @EndSurrogateId
```

### `_version`
```sql
WHERE ResourceTypeId = @RT AND ResourceId = @ID AND Version = @Version
```

---

## Special Search Parameters

### `_has` (Reverse Chaining)
```sql
-- _has:Observation:patient:code=1234-5
-- Reversed chain: Observation references this Patient
-- T1/T2 swapped in chain CTE
```

### `_include` / `_revinclude`
```sql
-- Include CTE (after @FilteredData persistence)
SELECT DISTINCT TOP (@IncludeCount+1) 
    refTarget.ResourceTypeId AS T1,
    refTarget.ResourceSurrogateId AS Sid1,
    0 AS IsMatch, 0 AS IsPartial
FROM dbo.ReferenceSearchParam refSource
JOIN dbo.Resource refTarget ON ...
WHERE refSource.ResourceTypeId IN (@MatchTypes)
  AND refSource.ResourceSurrogateId IN (SELECT Sid1 FROM @FilteredData WHERE IsMatch = 1)
```

### `_sort`
- `_sort=_lastUpdated`: Uses `ResourceSurrogateId ORDER BY` (no extra CTE)
- `_sort=name`: Two-phase sort with `Sort` or `SortWithFilter` CTE
- `_sort=-_lastUpdated`: Descending, uses same surrogate ID ordering

### `_total`
- `_total=accurate`: Second `CountOnly` query after results
- `_total=estimate`: Not supported in SQL provider (returns accurate)

### `_count`
- Maps to `SearchOptions.MaxItemCount`
- Emits `TOP (@MaxItemCount + 1)` to detect "has more pages"
