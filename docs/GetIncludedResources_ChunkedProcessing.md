# Chunked Processing Implementation - GetIncludedResources

## Critical Issue: Iterate Includes Across Pages

### The Problem

The original chunked implementation had a **fatal flaw** with iterative includes across multiple pages:

```
Page 1:
- Sources: Patient/1
- _include=Patient:organization → Organization/100 (found, returned)
- _include:iterate=Organization:partOf → Organization/200 (referenced by Org/100, returned)
- Page fills at 50 resources

Page 2 Request:
- Sources: ONLY Patient/1 (❌ Organization/100 is LOST!)
- _include:iterate=Organization:partOf → Can't find children of Org/200!
  - Organization/100 was found in Page 1 but isn't available as a source
  - Org/200's children (Org/300, Org/400...) won't be found
```

**Why This Happens:**
- Non-iterate includes use the original search results as sources
- **Iterate includes need resources from ALL PREVIOUS PAGES** as sources
- Continuation tokens only tracked position, not accumulated sources

### The Solution: Dual Source Tracking

The stored procedure now accepts **two separate source parameters**:

1. **`@SourceResources`**: Original search results (unchanged across pages)
2. **`@IterateSourceResources`**: Resources found by includes in previous pages

**New Calling Pattern:**

```csharp
// Page 1: First call
var page1Results = GetIncludedResources(
    sourceResources: mainSearchResults,           // Patient/1
    iterateSourceResources: EMPTY,                // First page has none
    includeSpecs: specs,
    includeCount: 50,
    lastCompletedIncludeId: null,
    continuationToken: null
);
// Returns: [Org/100, Org/200, ... more resources]

// Page 2: Accumulate ALL previous include results
var allPreviousIncludes = page1Results;  // Org/100, Org/200, etc.

var page2Results = GetIncludedResources(
    sourceResources: mainSearchResults,           // Still Patient/1
    iterateSourceResources: allPreviousIncludes,  // ✅ Org/100, Org/200 available!
    includeSpecs: specs,
    includeCount: 50,
    lastCompletedIncludeId: lastCompletedId,
    continuationToken: token
);
// Now can find: Org/300, Org/400 (children of Org/200)

// Page 3: Accumulate even more
allPreviousIncludes.AddRange(page2Results);

var page3Results = GetIncludedResources(
    sourceResources: mainSearchResults,
    iterateSourceResources: allPreviousIncludes,  // Growing set
    includeSpecs: specs,
    includeCount: 50,
    lastCompletedIncludeId: lastCompletedId,
    continuationToken: token
);
```

## Revised Continuation Token Strategy

### Token Components

**Old (broken)**: `"IncludeId|ExecutionLevel|ResourceTypeId|ResourceSurrogateId"`  
**New (simpler)**: `"ResourceTypeId|ResourceSurrogateId"` + `@LastCompletedIncludeId`

**Why the change?**
- Caller must track accumulated sources anyway (for iterate includes)
- Simpler to track which includes are **fully completed** vs in-progress
- Token only needs to track position within **current** include

### Token Usage

**Two-part continuation state:**

1. **`@LastCompletedIncludeId`** (int): Last include spec that was fully processed
   - Example: `2` means includes #1 and #2 are done, resume at #3

2. **`@IncludesContinuationToken`** (varchar): Position within the **next** include
   - Example: `"34|12345"` = ResourceTypeId 34, SurrogateId 12345
   - Only relevant if current include is partially complete

### Processing Flow Example

**Scenario**: 50 resources per page, iterative organization hierarchy

**Page 1**:
```
Input:
  @SourceResources: [Patient/1]
  @IterateSourceResources: []
  @LastCompletedIncludeId: NULL

Include #1: _include=Patient:organization
  → Find Organization/100 (referenced by Patient/1)
  → Add to page (1/51)

Include #2: _include:iterate=Organization:partOf
  → Source set: [Patient/1, Organization/100] ✅
  → Find Organization/200, 201, 202... (parent orgs)
  → Add 50 to page (51/51) ✓ PAGE FULL

Output:
  Resources: [Org/100, Org/200, Org/201, ..., Org/249] (51 total)
  LastCompletedIncludeId: 1  (Include #1 fully done)
  ContinuationToken: "67|249" (Include #2 partial, stopped at Org/249)
```

**Page 2**:
```
Input:
  @SourceResources: [Patient/1]  (unchanged)
  @IterateSourceResources: [Org/100, Org/200, ..., Org/249]  ✅ From Page 1!
  @LastCompletedIncludeId: 1
  @ContinuationToken: "67|249"

Resume Include #2: _include:iterate=Organization:partOf
  → Source set: [Patient/1, Org/100, Org/200, ..., Org/249] ✅ Full chain!
  → Resume after "67|249"
  → Find Org/250-299 (50 more parent orgs)
  → Add 50 to page (50/51)

Include #3: _include:iterate=Organization:partOf (next level)
  → Source set includes Org/250-299 ✅
  → Find 1 more resource
  → Page full (51/51)

Output:
  Resources: [Org/250, Org/251, ..., Org/299, Org/300]
  LastCompletedIncludeId: 2
  ContinuationToken: "67|300"
```

## Memory & Performance Benefits

| Metric | Original (Load All) | Chunked (Fixed) |
|--------|---------------------|-----------------|
| **Memory per call** | O(total results) | O(page size) |
| **Caller memory (iterate sources)** | N/A | O(pages × page size) |
| **First page processing** | All results | ~page size |
| **Cross-page iterate** | ❌ Broken | ✅ Works correctly |

### Trade-off: Caller Responsibility

**Price of correctness**: The caller must now:

1. **Accumulate include results** across pages
2. **Pass them back** as `@IterateSourceResources` on subsequent calls
3. **Track completion state** (`@LastCompletedIncludeId`)

**Why it's acceptable**:
- Caller already processes results (for merging with main search)
- Memory growth is bounded by include count × pages (typically small)
- Enables stateless stored procedure (simpler, more reliable)
- Alternative would require temp tables (session state = complexity)

### Example Memory Usage

**Scenario**: 10,000 total include results, 50 per page

**Per-call memory**:
- Stored procedure: 51 rows max (`@PageResults`)
- Caller accumulation: Page 1: 50, Page 2: 100, ... Page 200: 10,000

**Total system memory**: Same as loading all 10,000 (but spread across calls)

**Key difference**: Stored procedure stays bounded, caller controls accumulation

## Implementation Details

### Source Selection Logic

```sql
DECLARE @AllSourceResources TABLE (
    ResourceTypeId smallint,
    ResourceSurrogateId bigint,
    ResourceId varchar(64),
    IsIterateSource bit  -- 0 = original, 1 = from previous pages
)

-- Load original sources
INSERT INTO @AllSourceResources
SELECT ..., 0 FROM @SourceResources

-- Load iterate sources from previous pages
INSERT INTO @AllSourceResources  
SELECT ..., 1 FROM @IterateSourceResources

-- Usage in includes
WHERE (@CurrentIsIterate = 1 OR asr.IsIterateSource = 0)
```

**Logic**:
- **Non-iterate includes**: Only use `IsIterateSource = 0` (original search results)
- **Iterate includes**: Use **both** sources (full chain)

### Resumption Within an Include

```sql
WHERE (@CurrentIncludeId > @StartIncludeId
       OR @ContinuationToken IS NULL
       OR target.ResourceTypeId > @ContinuationResourceTypeId
       OR (target.ResourceTypeId = @ContinuationResourceTypeId 
           AND target.ResourceSurrogateId > @ContinuationResourceSurrogateId))
```

**Behavior**:
- If processing a new include (`@CurrentIncludeId > @StartIncludeId`): Start from beginning
- If resuming same include: Skip to resources after continuation point
- Ordering by `ResourceTypeId, ResourceSurrogateId` ensures consistency

### Deduplication Strategy

```sql
-- Exclude resources already in current page
WHERE NOT EXISTS (
    SELECT 1 FROM @PageResults pr
    WHERE pr.ResourceTypeId = target.ResourceTypeId
    AND pr.ResourceSurrogateId = target.ResourceSurrogateId
)
-- Exclude original and iterate sources (don't return them again)
AND NOT EXISTS (
    SELECT 1 FROM @AllSourceResources asr
    WHERE asr.ResourceTypeId = target.ResourceTypeId
    AND asr.ResourceSurrogateId = target.ResourceSurrogateId
)
```

**Why both checks?**
- `@PageResults`: Prevent duplicates within current page
- `@AllSourceResources`: Don't return search results again (already have them)

### Cursor vs Set-Based

We use a cursor for include specs (not resources):
```sql
DECLARE include_cursor CURSOR FOR
SELECT ... FROM @ExecutionOrder  -- Small table (~5-20 rows typical)
```

**Why it's okay:**
- Cursor over small metadata table (include specs)
- NOT over resource data (which is large)
- Each iteration processes a set-based query for resources
- Allows early exit between include specs

## Testing Considerations

### Critical Test Cases

1. **Single include, full page**: Verify basic functionality
2. **Iterate include spanning pages**: ✅ **Most Important**
   - Page 1 finds Org/100
   - Page 2 must find Org/100's children using accumulated sources
3. **Mixed iterate and non-iterate**: Verify source selection logic
4. **Deep hierarchy (3+ levels)**: Multi-level iterate chains
5. **Circular references**: A→B→C→A (should not infinite loop)
6. **Resume mid-include**: Continuation token within one include spec
7. **Large iterate source set**: Thousands of resources from previous pages

### Verification Points

**For iterate includes across pages**:
```csharp
// Page 1
var page1 = GetIncludedResources(sources, EMPTY, specs, 50, null, null);
Assert.Contains(page1, r => r.Id == "Organization/100");

// Page 2 - critical test
var page2 = GetIncludedResources(
    sources,              // Original: Patient/1
    page1,                // ✅ Must include Org/100!
    specs, 50, 
    lastCompletedId, 
    token
);

// Verify Org/100's children are found
Assert.Contains(page2, r => r.Id == "Organization/200");  // Parent of Org/100
```

### Performance Tests

Compare:
- First page retrieval time
- Memory usage during execution
- Query plans (should use indexes)
- Continuation token size
- End-to-end multi-page retrieval

## Migration Path

### Caller Responsibilities

The calling code must now:

1. **Track accumulated includes**:
```csharp
var allIncludeResults = new List<Resource>();
var lastCompletedIncludeId = (int?)null;
var continuationToken = (string)null;

while (hasMore)
{
    var pageResults = await GetIncludedResources(
        originalSearchResults,     // Unchanged
        allIncludeResults,          // ✅ Grows each page
        includeSpecs,
        pageSize: 50,
        lastCompletedIncludeId,
        continuationToken
    );

    // Add to accumulator for next page
    allIncludeResults.AddRange(pageResults);

    // Update continuation state from response headers or metadata
    lastCompletedIncludeId = GetLastCompletedId(response);
    continuationToken = GetContinuationToken(response);
    hasMore = pageResults.Count > pageSize;
}
```

2. **Return continuation info to caller**:
```csharp
// In response headers or bundle
response.Headers.Add("X-Last-Completed-Include-Id", lastCompletedIncludeId.ToString());
response.Headers.Add("X-Include-Continuation-Token", continuationToken);
```

3. **Parse continuation from request**:
```csharp
// When client requests next page
var request = ParseRequest();
lastCompletedIncludeId = request.Headers["X-Last-Completed-Include-Id"];
continuationToken = request.Headers["X-Include-Continuation-Token"];
```

### Changes Required

**Stored Procedure Call**:
```csharp
// Old (broken for iterate)
var results = await connection.ExecuteStoredProcAsync(
    "dbo.GetIncludedResources",
    new {
        SourceResources = sources,
        IncludeSpecs = specs,
        IncludeCount = 50,
        ContinuationToken = token
    }
);

// New (correct)
var results = await connection.ExecuteStoredProcAsync(
    "dbo.GetIncludedResources",
    new {
        SourceResources = originalSearchResults,
        IterateSourceResources = accumulatedIncludes,  // ✅ New!
        IncludeSpecs = specs,
        IncludeCount = 50,
        LastCompletedIncludeId = lastCompletedId,     // ✅ New!
        ContinuationToken = token                      // ✅ Simplified!
    }
);
```

**No changes needed**:
- Include expression parsing
- Result merging with main search
- Resource deduplication in response
- FHIR bundle assembly

### Performance Considerations

**Caller memory growth**:
- First page: 50 resources
- Second page: 100 resources (50 + 50)
- Nth page: N × 50 resources

**Mitigation strategies**:
1. **Limit total include pages**: Cap at reasonable number (e.g., 10 pages = 500 resources)
2. **Client-side pagination**: Include continuation requires client to track state anyway
3. **Cache accumulated sources**: Use distributed cache if needed (serialized ResourceKeyList)
