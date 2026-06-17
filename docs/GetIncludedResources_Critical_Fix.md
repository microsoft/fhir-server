# Critical Fix: Iterate Includes Across Pages

## The Bug

The second iteration of the stored procedure had a **fatal flaw** that broke iterate includes when results spanned multiple pages.

### What Went Wrong

```
Page 1:
  Input: Patient/1
  _include=Patient:organization → finds Organization/100
  _include:iterate=Organization:partOf → finds Organization/200 (parent of Org/100)
  Returns: [Org/100, Org/200, ...]

Page 2:
  Input: STILL ONLY Patient/1 ❌
  _include:iterate=Organization:partOf → CAN'T find children of Org/200!
    Reason: Org/100 was found in Page 1, but is NOT in the source set for Page 2
    Result: Iteration chain breaks - resources reachable from Org/200 are lost
```

### Root Cause

**Iterate includes** need to traverse from resources found by **previous includes**, not just the original search results.

The second implementation:
- ✅ Correctly chunked processing (memory efficient)
- ❌ Lost iterate sources between pages (correctness bug)

## The Fix

### Changed Parameters

```sql
-- BEFORE (broken)
CREATE PROCEDURE GetIncludedResources
    @SourceResources ResourceKeyList READONLY,  -- Only original search results
    ...

-- AFTER (fixed)
CREATE PROCEDURE GetIncludedResources
    @SourceResources ResourceKeyList READONLY,        -- Original search results
    @IterateSourceResources ResourceKeyList READONLY, -- ✅ Resources from previous pages!
    ...
```

### New Calling Pattern

The caller must now **accumulate and pass back** all previously found include results:

```csharp
var originalSources = ExecuteMainSearch();  // Patient/1
var accumulatedIncludes = new List<Resource>();

// Page 1
var page1 = GetIncludedResources(
    sourceResources: originalSources,        // [Patient/1]
    iterateSourceResources: [],              // Empty on first page
    ...
);
// page1 = [Org/100, Org/200, ...]

accumulatedIncludes.AddRange(page1);

// Page 2  
var page2 = GetIncludedResources(
    sourceResources: originalSources,        // Still [Patient/1]
    iterateSourceResources: accumulatedIncludes,  // ✅ [Org/100, Org/200, ...] !
    ...
);
// page2 can now find children of Org/200 because Org/100 is in the source set!
```

### Internal Logic

```sql
-- Combine both source sets
INSERT INTO @AllSourceResources
SELECT *, 0 AS IsIterateSource FROM @SourceResources           -- Original
UNION
SELECT *, 1 AS IsIterateSource FROM @IterateSourceResources   -- Accumulated

-- Use appropriate sources per include type
WHERE (@CurrentIsIterate = 1 
       OR asr.IsIterateSource = 0)  -- Non-iterate: only original sources
                                     -- Iterate: both sources
```

## Trade-offs

### What We Gained
- ✅ **Correctness**: Iterate includes work across pages
- ✅ **Efficiency**: Still chunked, memory-bounded per call
- ✅ **Stateless SP**: No temp tables, session state, or cleanup needed

### What We Lost
- ❌ **Caller complexity**: Must track accumulated sources
- ❌ **Memory on caller**: Grows with include results (bounded by total includes)

### Why This Is Acceptable

1. **Caller already processes results** (for merging with main search)
2. **Include counts are typically small** (hundreds, not millions)
3. **Alternative is worse**: Temp tables add session state, complexity, cleanup issues
4. **Correctness > convenience**: Broken iterate includes are unacceptable

## Comparison: 3 Iterations

| Aspect | V1: Load All | V2: Chunked (Broken) | V3: Chunked + Accumulated |
|--------|--------------|----------------------|---------------------------|
| **SP Memory** | O(total) | O(page) ✅ | O(page) ✅ |
| **Caller Memory** | N/A | N/A | O(pages × pagesize) |
| **Iterate Across Pages** | N/A | ❌ Broken | ✅ Works |
| **Early Termination** | ❌ No | ✅ Yes | ✅ Yes |
| **Session State** | None | None | None ✅ |

## Migration Checklist

- [ ] Update stored procedure signature (add `@IterateSourceResources` parameter)
- [ ] Modify calling code to accumulate include results
- [ ] Pass accumulated results on subsequent pages
- [ ] Update continuation token handling (now two-part: `LastCompletedIncludeId` + `Token`)
- [ ] Add integration test for iterate includes spanning pages
- [ ] Document caller responsibility for source accumulation

## Test Case: Critical Path

```csharp
[Fact]
public async Task IterateInclude_AcrossPages_FindsNestedResources()
{
    // Setup: Patient/1 → Organization/100 → Organization/200
    var patient = CreatePatient("Patient/1");
    var org100 = CreateOrganization("Organization/100", partOf: "Organization/200");
    var org200 = CreateOrganization("Organization/200");

    var searchResults = new[] { patient };
    var includeSpecs = new[] {
        new IncludeSpec { /* Patient:organization */ },
        new IncludeSpec { /* Organization:partOf (iterate) */ }
    };

    // Page 1: Should find Org/100
    var page1 = await GetIncludedResources(
        searchResults, 
        iterateSources: Array.Empty<Resource>(), 
        includeSpecs, 
        pageSize: 1,  // Force pagination
        null, 
        null
    );

    Assert.Contains(page1, r => r.Id == "Organization/100");

    // Page 2: MUST find Org/200 (referenced by Org/100)
    var page2 = await GetIncludedResources(
        searchResults,
        iterateSources: page1,  // ✅ Pass Org/100 back!
        includeSpecs,
        pageSize: 50,
        lastCompletedId: 1,
        token: null
    );

    // This assertion would FAIL in V2, but PASSES in V3
    Assert.Contains(page2, r => r.Id == "Organization/200");
}
```

## Conclusion

This fix represents a **fundamental correctness issue**, not just an optimization. Without it, iterate includes silently return incomplete results when pagination is involved - a subtle but critical bug that would be very difficult to diagnose in production.

The trade-off (caller must accumulate sources) is necessary and acceptable given:
- Correctness is non-negotiable
- Caller already processes results anyway
- Include result sets are typically manageable in size
- Alternative (session state) is architecturally worse
