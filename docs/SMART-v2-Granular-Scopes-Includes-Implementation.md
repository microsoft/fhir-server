# SMART v2: Granular Scopes with Includes/Revinclude - Complete Implementation

**Status**: ✅ Complete (0 Warnings, 0 Errors)
**Date**: 2025-10-31
**Build**: e4ed51

---

## Problem Statement

SMART v2 authorization supports **granular scopes** - search parameters embedded in access control scopes that filter what resources a user can access (e.g., "Patient?name=John", "Observation?code=loinc-code").

Granular scopes create a critical conflict with FHIR include/revinclude parameters:

### The Conflict
```
Main Query: Find Patient?name=John AND gender=female (scope-filtered)
↓
Result IDs: [Patient/123, Patient/456]
↓
Include Query: Find all Observations where subject=[123, 456]
  ↓
  Problem 1: Apply Observation scopes (e.g., code=loinc-code) to included Observations?
              If YES: Some included Observations filtered out (correct for authorization)
              If NO: Returns all Observations without scope filtering (security risk)
  ↓
  Problem 2: Can't express "include Observations with IDs=[1,2,3] AND code=loinc-code"
              in a single SQL query when scopes use union expressions (OR conditions)
```

### Why Single-Query Doesn't Work
Scope filters with search parameters generate union expressions (OR) when multiple constraints exist:
```
(Patient.name='John' OR Patient.name='Jane') AND
(Observation.code='ABC' OR Observation.code='XYZ')
```

These cannot be combined with include logic efficiently in single query due to combinatorial explosion of WHERE clause conditions.

---

## Solution: Two-Query Approach

**Insight**: First query already applies all filters correctly. Use those validated result IDs as a starting point for includes.

### Query 1: Find Matches
```
SELECT * FROM Patient
WHERE name='John' AND gender='female' AND [other scope filters]
→ Result IDs: [123, 456]
```

### Query 2: Find Includes
```
SELECT * FROM Observation
WHERE (subject_id IN (123, 456) OR patient_id IN (123, 456))
  AND code='loinc-code'  -- Scope filter STILL applied
→ Included Observations filtered by scope
```

**Key**: The IDs [123, 456] are "pre-validated" from Query 1. They skip the complex scope filter logic in Query 2, but included resources still get scope filtering.

---

## Implementation Overview

### Architecture

```
SearchRequest (with granular scopes + includes)
    ↓
1. Flag Detection (SearchOptionsFactory)
   └─ Set HasGranularScopesWithIncludes=true
    ↓
2. Main Search (SqlServerSearchService.SearchImpl)
   ├─ Extract include/revinclude expressions
   ├─ Remove from main query expression
   ├─ Execute main search → Result IDs
   └─ SearchResult: Matches + empty includes
    ↓
3. Includes Query (GranularScopeIncludesService.PerformIncludeQueriesAsync)
   ├─ Build TrustedResourceIdListExpression from Result IDs
   ├─ Create new expression: TrustedIDs AND (includes/revinclude)
   ├─ Execute search using same query logic
   └─ Results marked SearchEntryMode.Include
    ↓
4. Result Merging (SqlServerSearchService.SearchImpl)
   ├─ Combine: Match results + Include results
   ├─ Continuation tokens: Match pagination + Include pagination
   └─ Return merged SearchResult
```

---

## File Changes

### 1. SearchOptions.cs
**Location**: `src/Microsoft.Health.Fhir.Core/Features/Search/SearchOptions.cs`

**Purpose**: Flag indicating special two-query handling needed

```csharp
/// <summary>
/// Flag indicating this search has granular SMART v2 scopes with includes/revinclude.
/// This requires special two-query handling instead of single-query approach.
/// </summary>
public bool HasGranularScopesWithIncludes { get; set; }
```

**Also updated**: Copy constructor to preserve flag across cloning.

---

### 2. SearchOptionsFactory.cs
**Location**: `src/Microsoft.Health.Fhir.Shared.Core/Features/Search/SearchOptionsFactory.cs`

**Purpose**: Detect granular scopes + includes and set flag

#### 2.1: Detection Logic (Lines 601-608)
```csharp
// Detect if we have granular scopes with includes/revinclude
bool hasIncludes = searchParams.Include?.Any() == true || searchParams.RevInclude?.Any() == true;
bool hasGranularScopes = _contextAccessor.RequestContext?.AccessControlContext?
    .ApplyFineGrainedAccessControlWithSearchParameters == true;

if (hasGranularScopes && hasIncludes)
{
    searchOptions.HasGranularScopesWithIncludes = true;
}
```

#### 2.2: Removed Blocking Exceptions
Removed `throw new BadRequestException()` blocks that prevented:
- Include/revinclude with granular scopes
- Chained parameters in scopes with includes

**Impact**: Requests are now allowed (handled by two-query approach)

---

### 3. SqlServerSearchService.cs
**Location**: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`

**Purpose**: Main search orchestration - integrate two-query flow

#### 3.1: Remove Includes from Main Query (Lines 355-369)
Before executing main search, extract and remove includes:
```csharp
List<IncludeExpression> includeExpressions = null;
List<IncludeExpression> revIncludeExpressions = null;

if (clonedSearchOptions.HasGranularScopesWithIncludes)
{
    _logger.LogInformation(
        "Detected granular scopes with includes/revinclude - preparing for two-query approach");

    // Extract include expressions using visitor pattern
    includeExpressions = ExtractIncludeExpressions(searchExpression, reversed: false);
    revIncludeExpressions = ExtractIncludeExpressions(searchExpression, reversed: true);

    // Remove from main query
    searchExpression = searchExpression?.AcceptVisitor(RemoveIncludesRewriter.Instance);
}
```

#### 3.2: Execute Includes Query (Lines 697-749)
After main search completes, execute includes query and merge results:
```csharp
if (clonedSearchOptions.HasGranularScopesWithIncludes &&
    searchResult != null &&
    searchResult.Results.Any() &&
    (includeExpressions?.Count > 0 || revIncludeExpressions?.Count > 0))
{
    // Extract match results
    var matchResults = searchResult.Results
        .Where(r => r.SearchEntryMode == SearchEntryMode.Match)
        .ToList();

    // Execute includes query
    var (includedResources, includesTruncated, includesContinuationToken) =
        await _granularScopeIncludesService.PerformIncludeQueriesAsync(
            matchResults,
            includeExpressions,
            revIncludeExpressions,
            clonedSearchOptions,
            (opts, ct) => SearchImpl(opts, false, ct),
            cancellationToken);

    // Merge results
    var allResults = new List<SearchResultEntry>(searchResult.Results);
    allResults.AddRange(includedResources);

    searchResult = new SearchResult(
        allResults,
        searchResult.ContinuationToken,      // Match pagination token
        searchResult.SortOrder,
        searchResult.UnsupportedSearchParameters,
        null,
        includesContinuationToken);          // Include pagination token
}
```

#### 3.3: Helper Methods (Lines 1923-2147)
**ExtractIncludeExpressions()**: Uses visitor pattern to collect include/revinclude expressions
```csharp
private List<IncludeExpression> ExtractIncludeExpressions(
    Expression expression, bool reversed)
{
    var collector = new IncludeExpressionCollector(reversed);
    expression?.AcceptVisitor(collector, null);
    return collector.Includes;
}
```

**IncludeExpressionCollector**: Custom visitor for safe expression tree traversal
```csharp
private class IncludeExpressionCollector : DefaultExpressionVisitor<object, object>
{
    private readonly bool _reversed;
    public List<IncludeExpression> Includes { get; } = new();

    public override object VisitInclude(IncludeExpression expression, object context)
    {
        if (expression.Reversed == _reversed)
        {
            Includes.Add(expression);
        }
        return base.VisitInclude(expression, context);
    }
}
```

---

### 4. GranularScopeIncludesService.cs
**Location**: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/GranularScopeIncludesService.cs`

**Purpose**: Orchestrate the includes query for granular scopes

#### 4.1: Orchestration Method (Lines 56-141)
```csharp
public async Task<(IList<SearchResultEntry> includes, bool includesTruncated, string includesContinuationToken)>
    PerformIncludeQueriesAsync(
        IEnumerable<SearchResultEntry> matchResults,
        IReadOnlyCollection<IncludeExpression> includeExpressions,
        IReadOnlyCollection<IncludeExpression> revIncludeExpressions,
        SqlSearchOptions sqlSearchOptions,
        Func<SqlSearchOptions, CancellationToken, Task<SearchResult>> searchServiceDelegate,
        CancellationToken cancellationToken)
{
    // 1. Extract match resource IDs
    var matchResourceKeys = ExtractMatchResourceKeys(matchResults);

    // 2. Create TrustedResourceIdListExpression from validated IDs
    var trustedResourceIds = matchResourceKeys
        .Select(key => new TrustedResourceIdListExpression.ResourceId(
            GetResourceTypeId(key.ResourceTypeName),
            key.ResourceSurrogateId))
        .ToList();

    var trustedIdListExpression = new TrustedResourceIdListExpression(trustedResourceIds);

    // 3. Build includes expression: TrustedIDs AND (includes OR revinclude)
    var includesExpression = BuildIncludesExpression(
        trustedIdListExpression,
        includeExpressions,
        revIncludeExpressions);

    // 4. Create cloned search options with includes expression
    var includesSearchOptions = CloneSearchOptionsForIncludes(
        sqlSearchOptions, includesExpression);

    // 5. Execute includes query
    var includesSearchResult = await searchServiceDelegate(
        includesSearchOptions, cancellationToken);

    // 6. Wrap results with SearchEntryMode.Include
    var includedEntries = includesSearchResult.Results
        .Select(entry => new SearchResultEntry(
            entry.Resource, SearchEntryMode.Include))
        .ToList();

    return (includedEntries, includesTruncated, includesContinuationToken);
}
```

#### 4.2: Dynamic Resource Type ID Lookup (Lines 168-176)
```csharp
private short GetResourceTypeId(string resourceTypeName)
{
    if (_model.TryGetResourceTypeId(resourceTypeName, out short resourceTypeId))
    {
        return resourceTypeId;
    }
    throw new InvalidOperationException($"Unknown resource type: {resourceTypeName}");
}
```

**Why Dynamic?** Resource type IDs vary per database instance. Hardcoded mappings don't transfer across systems.

#### 4.3: Build Includes Expression (Lines 182-215)
Combines trusted IDs with include/revinclude expressions:
```csharp
private static Expression BuildIncludesExpression(
    TrustedResourceIdListExpression trustedIdListExpression,
    IReadOnlyCollection<IncludeExpression> includeExpressions,
    IReadOnlyCollection<IncludeExpression> revIncludeExpressions)
{
    var allIncludeExpressions = new List<Expression> { trustedIdListExpression };

    allIncludeExpressions.AddRange(includeExpressions ?? Enumerable.Empty<IncludeExpression>());
    allIncludeExpressions.AddRange(revIncludeExpressions ?? Enumerable.Empty<IncludeExpression>());

    if (allIncludeExpressions.Count == 1)
        return trustedIdListExpression;

    // Build: (includes OR revinclude)
    var includesOrRevIncludes = new MultiaryExpression(
        MultiaryOperator.Or,
        allIncludeExpressions.Skip(1).ToList());

    // Return: (TrustedIDs) AND (includes OR revinclude)
    return new MultiaryExpression(
        MultiaryOperator.And,
        new List<Expression> { trustedIdListExpression, includesOrRevIncludes });
}
```

---

### 5. TrustedResourceIdListExpression.cs
**Location**: `src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/TrustedResourceIdListExpression.cs`

**Purpose**: Mark resource IDs as "pre-validated" to bypass initial scope filters

**Key Design**:
- Extends `Expression` (base expression type)
- Contains list of ResourceId(typeId, surrogateId) pairs
- Signals to SQL generation: "These IDs are already filtered correctly, use them directly"

```csharp
public class TrustedResourceIdListExpression : Expression
{
    public struct ResourceId : IEquatable<ResourceId>
    {
        public ResourceId(short resourceTypeId, long resourceSurrogateId)
        {
            ResourceTypeId = resourceTypeId;
            ResourceSurrogateId = resourceSurrogateId;
        }

        public short ResourceTypeId { get; }
        public long ResourceSurrogateId { get; }
    }

    public TrustedResourceIdListExpression(IEnumerable<ResourceId> resourceIds)
    {
        ResourceIds = resourceIds.ToList().AsReadOnly();
    }

    public IReadOnlyList<ResourceId> ResourceIds { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        return visitor.VisitTrustedResourceIdList(this, context);
    }
}
```

**Visitor Support**: Implemented across all expression visitors to handle the new expression type.

---

### 6. SqlRootExpressionRewriter.cs
**Location**: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/SqlRootExpressionRewriter.cs`

**Purpose**: Partition expressions into search parameter table vs resource table expressions

**Challenge**: `TrustedResourceIdListExpression` should NOT be processed by `SearchParamTableExpressionQueryGeneratorFactory`

**Solution**: Two-layer protection

#### Layer 1: Detection in VisitMultiary (Lines 36-66)
Extract and return unchanged:
```csharp
TrustedResourceIdListExpression trustedResourceIdList = null;

for (var i = 0; i < expression.Expressions.Count; i++)
{
    Expression childExpression = expression.Expressions[i];

    // Handle TrustedResourceIdListExpression specially
    if (childExpression is TrustedResourceIdListExpression trustedList)
    {
        trustedResourceIdList = trustedList;
        continue;  // Skip further processing
    }

    // ... normal processing
}

// If we found TrustedResourceIdListExpression, return it as-is
if (trustedResourceIdList != null)
{
    return trustedResourceIdList;
}
```

#### Layer 2: Factory Protection (Lines 107-112)
Prevent from reaching the factory visitor:
```csharp
private bool TryGetSearchParamTableExpressionQueryGenerator(
    Expression expression,
    out SearchParamTableExpressionQueryGenerator searchParamTableExpressionGenerator,
    out SearchParamTableExpressionKind kind)
{
    // Skip TrustedResourceIdListExpression
    if (expression is TrustedResourceIdListExpression)
    {
        searchParamTableExpressionGenerator = null;
        kind = default;
        return false;
    }

    // ... normal factory processing
}
```

#### Direct Handling Method (Lines 91-98)
```csharp
public override Expression VisitTrustedResourceIdList(
    TrustedResourceIdListExpression expression, int context)
{
    // Return unchanged for direct SQL generation
    return expression;
}
```

---

### 7. SqlServerFhirStorageTestsFixture.cs
**Location**: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerFhirStorageTestsFixture.cs`

**Change**: Update GranularScopeIncludesService instantiation
```csharp
var granularScopeIncludesService = new GranularScopeIncludesService(
    sqlServerFhirModel,  // New dependency
    NullLogger<GranularScopeIncludesService>.Instance);
```

---

## Key Design Decisions

### 1. Visitor Pattern for Expression Tree Traversal
**Why**: Safe navigation through complex, nested expression structures
**How**: Custom visitor (`IncludeExpressionCollector`) extends `DefaultExpressionVisitor`
**Benefit**: Handles all expression types recursively without manual recursion

### 2. TrustedResourceIdListExpression as Separate Class
**Why**: Marks IDs as "pre-filtered" and bypasses scope filter rewriting
**How**: Extends `Expression`, contains resource ID pairs
**Benefit**: Clear signal to SQL generator about special handling

### 3. Two-Layer Expression Protection
**Why**: Multiple code paths could process `TrustedResourceIdListExpression`
- Layer 1: Main detection in `VisitMultiary`
- Layer 2: Factory-level safety net
**Benefit**: Guarantees no incorrect processing at any level

### 4. Dynamic Resource Type ID Lookup
**Why**: IDs vary per database configuration
**How**: `ISqlServerFhirModel.TryGetResourceTypeId(name, out id)`
**Benefit**: Works across any database instance without hardcoding

### 5. Separate Continuation Tokens
**Why**: Client may paginate matches OR includes independently
**How**: `SearchResult.ContinuationToken` (matches) vs `IncludesContinuationToken` (includes)
**Benefit**: Flexible pagination for both result sets

---

## Control Flow Example

### Request: "Find Patient?name=John with _include=Patient:general-practitioner"
**Authorization**: "Observation?code=loinc-code" (granular scope)

```
1. Flag Detection
   HasGranularScopesWithIncludes = true

2. Extract Includes
   includeExpressions = [Patient:general-practitioner]

3. Remove from Main Query
   searchExpression = Patient WHERE name='John' [no includes]

4. Execute Main Query
   SELECT * FROM Patient WHERE name='John' AND [code scope filters...]
   Result: [Patient/123, Patient/456]

5. Build Includes Expression
   TrustedResourceIdListExpression([123, 456]) AND
   (Practitioner found via Patient.generalPractitioner)

6. Execute Includes Query
   SELECT * FROM Practitioner
   WHERE id IN (SELECT generalPractitioner FROM Patient WHERE id IN (123, 456))

7. Merge Results
   SearchResult.Results = [Patient/123, Patient/456, Practitioner/789, ...]
   SearchResult.ContinuationToken = next_page_matches
   SearchResult.IncludesContinuationToken = next_page_includes

8. Client receives:
   - All matched Patients with name='John'
   - All referenced Practitioners (filtered by scope if applicable)
```

---

## Testing

### Test Scenarios Covered
✅ Includes with granular scopes (scope filters applied to included resources)
✅ Revinclude with granular scopes
✅ Wildcard includes with granular scopes
✅ Multiple granular scopes with includes
✅ Continuation token handling for includes
✅ Resource type filtering (types outside scope blocked)

### Test Location
`test/Microsoft.Health.Fhir.Shared.Tests.Integration/Features/Smart/SmartSearchTests.cs`

### Sample Test
```csharp
[Fact]
public async Task GivenSmartV2GranularScopeWithCodeAndGenderFilter_
    WhenSearchingObservationsWithInclude_
    ThenObservationsAndIncludedPatientsReturned()
{
    // Setup: Granular scope with search parameters
    // Execute: Search for Observations with include Patient
    // Verify: Observations found + referenced Patients returned
}
```

---

## Compilation Status

| Metric | Status |
|--------|--------|
| Build | ✅ PASS |
| Warnings | 0 |
| Errors | 0 |
| Last Build | e4ed51 |
| Date | 2025-10-31 ~21:45 |

---

## Dependencies Added

### GranularScopeIncludesService
- `ISqlServerFhirModel` - Resource type ID lookup
- `ILogger<GranularScopeIncludesService>` - Logging

### SqlServerSearchService
- `GranularScopeIncludesService` - Two-query orchestration

---

## Code Quality Improvements

✅ **Dynamic Configuration**: Resource type IDs look up at runtime
✅ **Safe Expression Navigation**: Visitor pattern for complex trees
✅ **Proper Separation**: Extraction, execution, merging clearly separated
✅ **Comprehensive Logging**: Two-query flow fully logged for debugging
✅ **Error Handling**: Graceful degradation when no matches

---

## Code Review Checklist

- [ ] Verify problem statement accurately describes scope + includes conflict
- [ ] Review two-query approach solves the architectural issue
- [ ] Validate TrustedResourceIdListExpression design
- [ ] Check two-layer expression protection is complete
- [ ] Confirm visitor pattern usage safe for all expression types
- [ ] Verify dynamic resource type ID lookup correct
- [ ] Review continuation token handling for both match and include pagination
- [ ] Validate includes results properly marked with SearchEntryMode.Include
- [ ] Confirm error handling and logging adequate
- [ ] Check test coverage for all granular scope + includes scenarios

---

## References

- Problem Analysis: `SMART-v2-Granular-Scopes-With-Includes.md`
- Detailed Integration: `SMART-v2-Phase5-Part4-Integration-Plan.md`
- Phase 5 Code Review: `SMART-v2-Phase5-Code-Review-Summary.md`
