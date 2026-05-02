---
name: fhir-sql-resource-crud-and-history
description: |
  Resource lifecycle management in the Microsoft FHIR Server SQL schema.
  Activate when: modifying MergeResources, working with ResourceSurrogateId, understanding
  transactions, invisible history, resource versioning, "MergeResourcesBeginTransaction",
  "MergeResourcesCommitTransaction", "Transactions table", "ResourceSurrogateId",
  "IsHistory", "RawResource = 0xF", "HistoryTransactionId", "ResourceChangeData",
  "CaptureResourceChanges", "KeepHistory", resource creation, resource update, resource delete,
  FHIR bundle transaction, conditional create, conditional update.
---

# FHIR SQL Resource Lifecycle

## When to use this skill

Use this skill whenever you need to:
- Modify the resource write path (MergeResources and related procedures)
- Understand ResourceSurrogateId construction and implications
- Work with the Transactions table visibility protocol
- Implement or modify invisible history behavior
- Add change capture for new resource operations

## Core invariants

1. **ResourceSurrogateId is immutable once assigned** — it serves as primary key, temporal ordering, and version identity. Construction: `datediff_big(millisecond, '0001-01-01', sysUTCdatetime()) * 80000 + NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence`. The sequence cycles 0-79999 with CACHE 1000000.

2. **The three-phase transaction protocol MUST be followed for all writes**:
   - Phase 1: `MergeResourcesBeginTransaction(@Count)` → reserves surrogate ID range, creates Transactions row
   - Phase 2: `MergeResources(...)` → processes all resources and search parameters
   - Phase 3: `MergeResourcesCommitTransaction(@TransactionId)` → makes resources visible

3. **Readers MUST check transaction visibility** — a resource is visible only when its transaction in `dbo.Transactions` is committed. Resources with uncommitted transactions are invisible to search.

4. **Invisible history uses `RawResource = 0xF` as sentinel** — when a resource update sets `KeepHistory = false`, the previous version's `RawResource` is replaced with `0xF` (15 bytes). The version metadata (Version, IsHistory, IsDeleted) is preserved, but the content is gone.

5. **ResourceChangeData capture is mandatory** — every create/update/delete MUST generate a row in `dbo.ResourceChangeData` via `CaptureResourceIdsForChanges`. Missing change capture breaks FHIR subscriptions and event notifications.

6. **Version conflicts use SearchParamHash for staleness detection** — the `SearchParamHash` column on Resource tracks whether extracted search parameters match the current search parameter definitions.

## Required patterns

### ResourceSurrogateId range for batch operations
```sql
-- In MergeResourcesBeginTransaction:
DECLARE @SurrogateIdRangeFirstValue bigint
SET @SurrogateIdRangeFirstValue = datediff_big(millisecond, '0001-01-01', sysUTCdatetime()) * 80000

-- Insert into Transactions table
INSERT INTO dbo.Transactions (SurrogateIdRangeFirstValue, SurrogateIdRangeLastValue)
VALUES (@SurrogateIdRangeFirstValue, @SurrogateIdRangeFirstValue + @Count - 1)
```

### Invisible history sentinel
```sql
-- When updating a resource with invisible history enabled:
UPDATE dbo.Resource
SET RawResource = 0xF,  -- Sentinel value: content purged
    IsHistory = 1
WHERE ResourceTypeId = @ResourceTypeId
  AND ResourceSurrogateId = @PreviousSurrogateId
```

### Resource version increment
```sql
-- New version gets a new ResourceSurrogateId (NOT incrementing the old one)
-- The old row gets IsHistory = 1, the new row gets IsHistory = 0
-- Version is incremented: @NewVersion = @CurrentVersion + 1
```

### Change capture integration
```sql
-- After resource insert/update/delete in MergeResources:
INSERT INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
SELECT ResourceId, ResourceTypeId, Version,
       CASE WHEN IsDeleted = 1 THEN 2  -- Deleted
            WHEN Version = 1 THEN 0     -- Created
            ELSE 1                       -- Updated
       END
FROM @ResourceList
```

## Common mistakes to avoid

- **Generating ResourceSurrogateId without the sequence**: Direct timestamp calculation without the uniquifier creates collisions under concurrent writes
- **Bypassing the Transactions table**: Resources inserted without a Transactions row are permanently invisible to readers that check transaction visibility
- **Forgetting to capture resource changes**: Missing ResourceChangeData rows break downstream subscribers
- **Modifying ResourceSurrogateId after insert**: It's the immutable identity — changing it breaks all search parameter foreign key relationships
- **Setting RawResource = NULL instead of 0xF**: The invisible history sentinel is specifically `0xF`, not NULL. NULL has different semantics
- **Incrementing Version without creating a new surrogate**: Each version gets its own ResourceSurrogateId — the old ID becomes historical

## Checklist before committing

- [ ] All resource writes go through MergeResources (not direct INSERT)
- [ ] ResourceSurrogateId generated correctly (timestamp * 80000 + sequence)
- [ ] Transactions table entry created for batch operations
- [ ] ResourceChangeData captured for every CUD operation
- [ ] Invisible history uses 0xF sentinel (not NULL)
- [ ] Version increment creates new surrogate ID
- [ ] IsHistory set correctly on old and new versions
- [ ] SearchParamHash updated when search parameters change

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResources.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResourcesBeginTransaction.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResourcesCommitTransaction.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/Resource.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/Transactions.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/ResourceChangeData.sql`
