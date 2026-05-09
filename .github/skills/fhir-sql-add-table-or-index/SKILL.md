---
name: fhir-sql-add-table-or-index
description: |
  Partitioning strategy and index conventions for the Microsoft FHIR Server SQL schema.
  Activate when: adding a search parameter table, creating an index on a FHIR table,
  modifying partition schemes, adding a new index, "PartitionScheme_ResourceTypeId",
  "ResourceTypeId", partition alignment, clustered index, filtered index, search param table,
  "DATA_COMPRESSION", "LOCK_ESCALATION = AUTO", index hint, partition elimination.
---

# FHIR SQL Partitioning and Indexing

## When to use this skill

Use this skill whenever you need to:
- Create a new search parameter table
- Add or modify an index on any partitioned table
- Understand the partition elimination contract
- Add index hints to queries on partitioned tables

## Core invariants

1. **Every search parameter table MUST be clustered on `(ResourceTypeId, ResourceSurrogateId, SearchParamId)`** on `PartitionScheme_ResourceTypeId(ResourceTypeId)` with `DATA_COMPRESSION = PAGE`. This is the non-negotiable clustered index shape.

2. **Every nonclustered search index MUST be partition-aligned via `ON PartitionScheme_ResourceTypeId(ResourceTypeId)`** — unaligned indexes prevent partition SWITCH operations and break the partitioning contract. Because the index is partition-aligned by `ResourceTypeId`, partition elimination happens automatically when the query has a `ResourceTypeId` predicate.

3. **Modern search-param tables do NOT carry an `IsHistory` column.** History is tracked on `dbo.Resource` only; the search-param tables are deleted/repopulated via `MergeResources` when a row's history changes. Do not add `IsHistory` to a new search-param table and do not add `WHERE IsHistory = 0` filters to its indexes. The one exception is `dbo.TokenText`, which retains `IsHistory` for legacy reasons — do not propagate that pattern to new tables.

4. **All indexes MUST use `DATA_COMPRESSION = PAGE`** — the EAV pattern has high repetition that benefits significantly from page compression.

5. **All partitioned tables MUST have `SET (LOCK_ESCALATION = AUTO)`** — escalates to partition-level locks rather than table-level.

6. **Filtered indexes are used for sparse columns, not for history.** Examples: `StringSearchParam.IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL` filters `WHERE TextOverflow IS NOT NULL`; `NumberSearchParam` uses `WHERE SingleValue IS NOT NULL` vs `WHERE SingleValue IS NULL` to split point-vs-range storage. Use a filtered index when a value is meaningfully sparse, not as a history workaround.

## Required patterns

### New search parameter table template
```sql
CREATE TABLE dbo.NewTypeSearchParam (
    ResourceTypeId           smallint         NOT NULL,
    ResourceSurrogateId      bigint           NOT NULL,
    SearchParamId            smallint         NOT NULL,
    -- Type-specific columns here. NOTE: no IsHistory column.
    Value1                   varchar(256)     COLLATE Latin1_General_100_CS_AS NOT NULL,
    Value1Overflow           varchar(max)     COLLATE Latin1_General_100_CS_AS NULL
)

ALTER TABLE dbo.NewTypeSearchParam SET (LOCK_ESCALATION = AUTO)

-- Clustered index (mandatory shape)
CREATE CLUSTERED INDEX IXC_NewTypeSearchParam
ON dbo.NewTypeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

-- Search index: partition-aligned, no IsHistory filter
CREATE INDEX IX_SearchParamId_Value1
ON dbo.NewTypeSearchParam (SearchParamId, Value1)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
```
Compare to canonical `dbo.TokenSearchParam` (no `IsHistory`, no `WHERE` filter on the search index, partition-aligned).

### Collation rules
```sql
-- IDs, codes, URIs: case-sensitive
Code varchar(256) COLLATE Latin1_General_100_CS_AS

-- Human-readable text (StringSearchParam):
Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC

-- Token text search:
Text nvarchar(256) COLLATE Latin1_General_CI_AI
```

### Index hint pattern on Resource table
```sql
SELECT r.ResourceSurrogateId, r.RawResource
FROM dbo.Resource r WITH (INDEX = IX_Resource_ResourceTypeId_ResourceSurrogateId)
WHERE r.ResourceTypeId = @ResourceTypeId
  AND r.ResourceSurrogateId BETWEEN @StartId AND @EndId
```

### The Number/Quantity range pattern
```sql
-- For range-valued parameters (Number, Quantity):
-- SingleValue stores the value when it's a point, LowValue/HighValue for ranges.
-- Use filtered indexes split by the sparse SingleValue column (NOT by IsHistory):
CREATE INDEX IX_SearchParamId_SingleValue
ON dbo.NumberSearchParam (SearchParamId, SingleValue)
WHERE SingleValue IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE INDEX IX_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam (SearchParamId, LowValue, HighValue)
WHERE SingleValue IS NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
```

## Common mistakes to avoid

- **Adding an `IsHistory` column to a new search-param table**: Modern search-param tables don't carry it. Search-param rows for historical resource versions are deleted by `MergeResources`, not flagged.
- **Adding `WHERE IsHistory = 0` to a search-param index**: Same reason — the column doesn't exist on most tables.
- **Non-aligned indexes**: Forgetting `ON PartitionScheme_ResourceTypeId(ResourceTypeId)` breaks partition SWITCH.
- **Wrong collation**: Using CI_AI for code/URI columns breaks FHIR's case-sensitive code matching.
- **Missing ResourceTypeId in queries**: Without it, SQL Server scans ALL partitions — 40x slower on large databases.
- **ROW compression instead of PAGE**: PAGE compression provides 2-3x better ratio on EAV data.
- **TABLE lock escalation**: Default is TABLE; must explicitly set AUTO on partitioned tables.
- **Unique indexes without partition key**: SQL Server requires the partition key in unique indexes on partitioned tables.

## Checklist before committing

- [ ] Clustered index shape is `(ResourceTypeId, ResourceSurrogateId, SearchParamId)`
- [ ] All indexes specify `ON PartitionScheme_ResourceTypeId(ResourceTypeId)`
- [ ] All indexes specify `DATA_COMPRESSION = PAGE`
- [ ] No `IsHistory` column on new search-param tables (TokenText is the only legacy exception)
- [ ] No `WHERE IsHistory = 0` filter added; filtered indexes only used for sparse value columns
- [ ] Table has `SET (LOCK_ESCALATION = AUTO)`
- [ ] Correct collation for each column type
- [ ] Corresponding TVP type created for MergeResources integration
- [ ] Corresponding delete/insert logic added to MergeResources and UpdateResourceSearchParams

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/TokenSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/StringSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/NumberSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/Resource.sql`
