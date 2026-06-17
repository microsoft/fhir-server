# GetIncludedResources Stored Procedure

## Overview

The `GetIncludedResources` stored procedure handles iterative include and reverse include (_include, _revinclude, _include:iterate, _revinclude:iterate) operations for FHIR search queries. It processes references between resources in a paginated manner to avoid loading too much data into memory at once and supports proper continuation tokens for multi-page searches.

## Purpose

This stored procedure addresses the limitations of dynamically generated SQL for include operations by:

1. **Handling iterative includes**: Processes _include:iterate and _revinclude:iterate which require multiple levels of reference resolution
2. **Dependency ordering**: Automatically determines the correct order to apply include specifications based on their dependencies
3. **Pagination support**: Returns one page of results at a time with continuation token support
4. **Memory efficiency**: Uses chunked processing to load only what's needed for the current page, avoiding pulling all resources into memory
5. **Circular reference handling**: Prevents infinite loops when resources reference each other
6. **Early termination**: Stops processing as soon as enough results are found for the requested page

## Parameters

### @SourceResources (dbo.ResourceKeyList)

The original search result resources that form the base for all include operations. This set remains constant across all pages of includes.

**Structure:**
```sql
ResourceTypeId   smallint
ResourceId       varchar(64)
Version          int (NULL for current version)
```

### @IterateSourceResources (dbo.ResourceKeyList)

Resources found by include operations in **previous pages** that should be used as sources for iterate includes in the current page. This enables multi-level traversal across page boundaries.

**Usage Pattern:**
- **Page 1**: Pass empty list
- **Page 2+**: Pass accumulated resources from all previous pages

**Why needed**: Iterate includes must traverse from resources found by earlier includes, not just the original search results.

**Example**:
```
Page 1: _include=Patient:organization finds Organization/100
Page 2: _include:iterate=Organization:partOf needs to traverse FROM Organization/100
        → Must pass Organization/100 in @IterateSourceResources!
```

### @IncludeSpecifications (dbo.IncludeSpecificationList)

A table-valued parameter defining all include and reverse include operations to perform.

**Structure:**
```sql
IncludeId            int        -- Unique identifier for this include
SourceResourceTypeId smallint   -- Resource type containing the reference (for include) or referenced (for revinclude)
SearchParamId        smallint   -- Search parameter ID (NULL for wildcard)
TargetResourceTypeId smallint   -- Target resource type (NULL for any type)
IsReversed           bit        -- 0 = _include, 1 = _revinclude
IsIterate            bit        -- 0 = normal, 1 = :iterate modifier
IsWildCard           bit        -- 1 for wildcard includes (e.g., _include=*)
```

**Examples:**

1. `_include=Patient:organization` (Patient references Organization)
```sql
IncludeId = 1
SourceResourceTypeId = <Patient TypeId>
SearchParamId = <organization param id>
TargetResourceTypeId = <Organization TypeId>
IsReversed = 0
IsIterate = 0
IsWildCard = 0
```

2. `_revinclude=Observation:patient` (Observation references Patient)
```sql
IncludeId = 2
SourceResourceTypeId = <Observation TypeId>
SearchParamId = <patient param id>
TargetResourceTypeId = <Patient TypeId>
IsReversed = 1
IsIterate = 0
IsWildCard = 0
```

3. `_include:iterate=Encounter:subject` (iterative include)
```sql
IncludeId = 3
SourceResourceTypeId = <Encounter TypeId>
SearchParamId = <subject param id>
TargetResourceTypeId = <Patient TypeId>
IsReversed = 0
IsIterate = 1
IsWildCard = 0
```

### @IncludeCount (int)

Maximum number of included resources to return in a single page. The procedure returns up to `@IncludeCount + 1` resources to allow the caller to detect if there are more pages.

### @LastCompletedIncludeId (int, optional)

The `IncludeId` of the last include specification that was **fully** completed in a previous page. The procedure will resume from the next include spec (or continue within the current one if `@IncludesContinuationToken` is provided).

**Usage:**
- **NULL**: Start from the first include spec
- **2**: Includes #1 and #2 are done, start from #3 (or continue #3 if token provided)

### @IncludesContinuationToken (varchar(500), optional)

Position within the **currently processing** include specification for resuming a partially completed include. Format: `"ResourceTypeId|ResourceSurrogateId"`.

Example: `"12|54321"`

**Note**: This token only tracks the position **within one include spec**, not which include spec is being processed (that's `@LastCompletedIncludeId`).

**Token Components:**
- **ResourceTypeId**: Last returned resource's type ID
- **ResourceSurrogateId**: Last returned resource's surrogate ID

## Return Value

Returns a result set with the following columns:

```sql
ResourceTypeId       smallint
ResourceId           varchar(64)
ResourceSurrogateId  bigint
Version              int
IsDeleted            bit
IsHistory            bit
RawResource          varbinary(max)
IsRawResourceMetaSet bit
SearchParamHash      varchar(64)
```

The result set is ordered by `ResourceTypeId, ResourceSurrogateId` to ensure consistent pagination.

**Pagination Detection:**
- If `COUNT(*) > @IncludeCount`, there are more results
- Determine `@LastCompletedIncludeId` by checking if last row's `IncludeId` filled the page
- The last row's `ResourceTypeId|ResourceSurrogateId` becomes the continuation token (if include not complete)

## Continuation State Management

The procedure uses a **two-part continuation mechanism**:

1. **Between includes**: `@LastCompletedIncludeId` (which includes are done)
2. **Within an include**: `@IncludesContinuationToken` (where within current include)

**Example Scenarios:**

**Scenario A: Include #2 Partially Complete**
```
Page ends mid-include #2:
  LastCompletedIncludeId: 1 (Include #1 fully done)
  ContinuationToken: "34|12345" (Last resource from include #2)

Next page resumes:
  - Skip includes #1 (done)
  - Resume include #2 after resource 34|12345
  - Then proceed to #3, #4, etc. until page full
```

**Scenario B: Include #2 Exactly Fills Page**
```
Page ends exactly at end of include #2:
  LastCompletedIncludeId: 2 (Includes #1 and #2 done)
  ContinuationToken: NULL (No partial include)

Next page starts fresh:
  - Skip includes #1, #2 (done)
  - Start include #3 from beginning
```

## Processing Logic

### 1. Source Combination

```sql
@AllSourceResources = @SourceResources + @IterateSourceResources
```

- **Original sources** (`@SourceResources`): Never changes across pages
- **Iterate sources** (`@IterateSourceResources`): Grows as caller accumulates results
- **Combined set** used for searching, with flag to distinguish types

### 2. Dependency Ordering

The procedure analyzes include specifications and determines execution order:

- **Level 0**: All non-iterate includes
- **Level 1+**: Iterate includes in dependency order

For example:
```
_include=Patient:organization          (Level 0)
_include:iterate=Organization:partOf   (Level 1 - depends on Level 0)
_revinclude=Observation:patient        (Level 0)
```

### 3. Include Processing with Source Selection

For each include specification (starting from `@LastCompletedIncludeId + 1`):

1. **Determine source set**:
   - **Non-iterate**: Use only `@SourceResources` (original search results)
   - **Iterate**: Use `@AllSourceResources` (original + accumulated)

2. **Execute query** to find referenced/referencing resources
3. **Add to page** (up to `@IncludeCount + 1`)
4. **Check if full**: Stop when page reaches limit
5. **Move to next include**: Continue until page full

**Why this matters**: Iterate includes across pages MUST have access to resources found earlier!

### 4. Pagination

For each include specification in execution order:

1. **Check remaining slots**: Calculate how many more resources can fit in the current page
2. **Process in chunks**: Fetch up to `@RemainingSlots` resources matching the include criteria
3. **For forward includes** (_include):
   - Join `ReferenceSearchParam` with source resources
   - Follow `ReferenceResourceTypeId` and `ReferenceResourceId` to target resources
   - Filter by `SearchParamId` (unless wildcard)
   - Filter by `TargetResourceTypeId` (if specified)
   - Apply continuation token offset (if resuming mid-include)

4. **For reverse includes** (_revinclude):
   - Join `ReferenceSearchParam` using target resources
   - Find resources that contain references to the targets
   - Filter by source type and search parameter
   - Apply continuation token offset (if resuming mid-include)

5. **Update tracking tables**:
   - Add found resources to `@PageResults` (output)
   - Add found resources to `@ProcessedResources` (for iterate includes)
   - Update `@RemainingSlots`

6. **Early exit**: If `@RemainingSlots <= 0`, stop processing and return results

**Key Optimizations**:
- Only processes include specs needed to fill the current page
- Tracks exact position within a partial include for resumption
- Deduplicates across chunks to prevent re-fetching

### 4. Pagination

Results are returned in a deterministic order (`ResourceTypeId, ResourceSurrogateId`) to ensure:
- Consistent pages across calls
- Reliable continuation token generation
- No duplicate or missing resources

## Usage Example

```sql
DECLARE @Sources dbo.ResourceKeyList
DECLARE @Includes dbo.IncludeSpecificationList

-- Main search returned these patients
INSERT INTO @Sources (ResourceTypeId, ResourceId, Version)
VALUES (12, 'patient-123', NULL), (12, 'patient-456', NULL)

-- _include=Patient:organization
INSERT INTO @Includes (IncludeId, SourceResourceTypeId, SearchParamId, TargetResourceTypeId, IsReversed, IsIterate, IsWildCard)
VALUES (1, 12, 45, 67, 0, 0, 0)

-- _revinclude=Observation:patient
INSERT INTO @Includes (IncludeId, SourceResourceTypeId, SearchParamId, TargetResourceTypeId, IsReversed, IsIterate, IsWildCard)
VALUES (2, 89, 23, 12, 1, 0, 0)

-- Get first page (max 50 resources)
EXEC dbo.GetIncludedResources 
    @SourceResources = @Sources,
    @IncludeSpecifications = @Includes,
    @IncludeCount = 50,
    @IncludesContinuationToken = NULL

-- If result has 51 rows, get next page using last row's data
EXEC dbo.GetIncludedResources 
    @SourceResources = @Sources,
    @IncludeSpecifications = @Includes,
    @IncludeCount = 50,
    @IncludesContinuationToken = '2|0|89|123456'  -- IncludeId|Level|TypeId|SurrogateId from last row
```

## Performance Considerations

1. **Chunked Processing**: Only loads resources needed for the current page, not all results
2. **Index Usage**: Uses specific indexes on `Resource` and `ReferenceSearchParam` tables
3. **MAXDOP 1**: Final SELECT uses `OPTION (MAXDOP 1)` for consistent ordering
4. **Memory Tables**: Uses table variables bounded to page size
5. **Circular References**: Limited to 10 execution levels
6. **Early Termination**: Stops immediately when page is full
7. **Continuation State**: Tracks exact position (include spec + offset) for efficient resumption

**Scalability Benefits**:
- Memory usage: O(page size) instead of O(total results)
- Processing time: Proportional to page size, not total result count
- Works efficiently even with thousands of included resources

## Error Handling

- Logs errors via `dbo.LogEvent`
- Re-throws errors to caller with original severity and state
- Special handling for error 1750 (actual error occurred before this)

## Integration Points

This stored procedure is designed to be called from:
- `SqlServerSearchService.SearchAsync` for include operations
- Include continuation token handling logic
- Multi-page search result assembly

The calling code should:
1. Convert `IncludeExpression` objects to `IncludeSpecificationList` rows
2. Handle continuation token serialization/deserialization
3. Merge included resources with main search results
4. Detect and handle pagination (when result count > requested count)
