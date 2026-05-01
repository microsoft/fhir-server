---
name: fhir-sql-partitioning-and-indexing
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

2. **Every nonclustered search index MUST include `ResourceTypeId`** — either as the partition key or in the INCLUDE list — to enable partition elimination.

3. **Search indexes MUST be filtered with `WHERE IsHistory = 0`** — historical versions are excluded from search.

4. **Every new index on a partitioned table MUST specify `ON PartitionScheme_ResourceTypeId(ResourceTypeId)`** — unaligned indexes prevent partition SWITCH operations.

5. **All indexes MUST use `DATA_COMPRESSION = PAGE`** — the EAV pattern has high repetition that benefits significantly from page compression.

6. **All partitioned tables MUST have `SET (LOCK_ESCALATION = AUTO)`** — escalates to partition-level locks rather than table-level.

## Required patterns

### New search parameter table template
```sql
CREATE TABLE dbo.NewTypeSearchParam (
    ResourceTypeId           smallint         NOT NULL,
    ResourceSurrogateId      bigint           NOT NULL,
    SearchParamId            smallint         NOT NULL,
    IsHistory                bit              NOT NULL,
    -- Type-specific columns here
    Value1                   varchar(256)     COLLATE Latin1_General_100_CS_AS NOT NULL,
    Value1Overflow           varchar(max)     NULL,
) ON PartitionScheme_ResourceTypeId(ResourceTypeId)

ALTER TABLE dbo.NewTypeSearchParam
  SET (LOCK_ESCALATION = AUTO)

-- Clustered index (mandatory shape)
CREATE CLUSTERED INDEX IXC_NewTypeSearchParam
ON dbo.NewTypeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

-- Search index (filtered, partition-aligned)
CREATE NONCLUSTERED INDEX IX_NewTypeSearchParam_SearchParamId_Value1
ON dbo.NewTypeSearchParam(SearchParamId, Value1)
INCLUDE (ResourceTypeId)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
```

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
-- SingleValue stores the value when it's a point, LowValue/HighValue for ranges
-- Use filtered indexes for each case:
CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam(SearchParamId, SingleValue)
INCLUDE (ResourceTypeId)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam(SearchParamId, LowValue, HighValue)
INCLUDE (ResourceTypeId)
WHERE IsHistory = 0 AND SingleValue IS NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
```

## Common mistakes to avoid

- **Non-aligned indexes**: Forgetting `ON PartitionScheme_ResourceTypeId(ResourceTypeId)` breaks partition SWITCH
- **Missing filtered WHERE clause**: Including historical versions in search indexes wastes space and slows queries
- **Wrong collation**: Using CI_AI for code/URI columns breaks FHIR's case-sensitive code matching
- **Missing ResourceTypeId in queries**: Without it, SQL Server scans ALL partitions — 40x slower on large databases
- **ROW compression instead of PAGE**: PAGE compression provides 2-3x better ratio on EAV data
- **TABLE lock escalation**: Default is TABLE; must explicitly set AUTO on partitioned tables
- **Unique indexes without partition key**: SQL Server requires the partition key in unique indexes on partitioned tables

## Checklist before committing

- [ ] Clustered index shape is `(ResourceTypeId, ResourceSurrogateId, SearchParamId)`
- [ ] All indexes specify `ON PartitionScheme_ResourceTypeId(ResourceTypeId)`
- [ ] All indexes specify `DATA_COMPRESSION = PAGE`
- [ ] Search indexes include `WHERE IsHistory = 0` filter
- [ ] Table has `SET (LOCK_ESCALATION = AUTO)`
- [ ] Correct collation for each column type
- [ ] ResourceTypeId included in nonclustered index (key or INCLUDE)
- [ ] Corresponding TVP type created for MergeResources integration
- [ ] Corresponding delete/insert logic added to MergeResources and UpdateResourceSearchParams

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/TokenSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/StringSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/NumberSearchParam.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/Resource.sql`
