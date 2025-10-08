# Impact Analysis: Changing SearchResourceHandler Constructor from ISearchService to IScopeProvider<ISearchService>

## Proposed Change

**Current:** 
```csharp
public SearchResourceHandler(ISearchService searchService, ...)
```

**Proposed:**
```csharp
public SearchResourceHandler(IScopeProvider<ISearchService> searchServiceFactory, ...)
```

## Executive Summary

⚠️ **HIGH IMPACT** - This change would fundamentally alter the lifetime management and dependency injection pattern for `SearchResourceHandler`, requiring significant refactoring across multiple components.

**Recommendation:** **DO NOT MAKE THIS CHANGE** unless there is a compelling architectural reason that justifies the extensive refactoring required.

---

## Current Architecture

### Service Registration
```csharp
// PersistenceModule.cs
services.AddFactory<IScoped<ISearchService>>();
```

This indicates that `ISearchService` is registered as a **scoped service**, meaning:
- One instance per HTTP request
- Automatically disposed at the end of the request
- Injected directly into consumers like `SearchResourceHandler`

### Current Lifetime Pattern
```
HTTP Request → DI Container creates Scoped Service Scope
    ↓
SearchResourceHandler created with ISearchService instance
    ↓
SearchService operates within request scope
    ↓
End of request → SearchService disposed automatically
```

---

## Proposed Architecture with IScopeProvider

### What IScopeProvider Does

`IScopeProvider<T>` is a **factory pattern** used in this codebase for creating scoped instances, typically for:
1. **Background jobs** that need to create their own service scopes
2. **Long-running operations** that outlive a single HTTP request
3. **Manual lifetime management** scenarios

```csharp
public interface IScopeProvider<T>
{
    IScoped<T> Invoke();
}

public interface IScoped<T> : IDisposable
{
    T Value { get; }
}
```

### New Lifetime Pattern
```
HTTP Request → SearchResourceHandler created with IScopeProvider<ISearchService>
    ↓
Handle() method called
    ↓
Must manually invoke: using (var scope = _searchServiceFactory.Invoke())
    ↓
Use: scope.Value.SearchAsync(...)
    ↓
Must dispose scope explicitly
```

---

## Impact Analysis

### 1. **SearchResourceHandler Changes** (BREAKING)

#### Current Code:
```csharp
public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, CancellationToken cancellationToken)
{
    // Direct usage
    SearchResult searchResult = await _searchService.SearchAsync(
        resourceType: request.ResourceType,
        queryParameters: request.Queries,
        cancellationToken: cancellationToken,
        isIncludesOperation: request.IsIncludesRequest);
    
    return new SearchResourceResponse(bundle);
}
```

#### Required New Code:
```csharp
private readonly IScopeProvider<ISearchService> _searchServiceFactory;

public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, CancellationToken cancellationToken)
{
    // Manual scope management required
    using (IScoped<ISearchService> searchServiceScope = _searchServiceFactory.Invoke())
    {
        SearchResult searchResult = await searchServiceScope.Value.SearchAsync(
            resourceType: request.ResourceType,
            queryParameters: request.Queries,
            cancellationToken: cancellationToken,
            isIncludesOperation: request.IsIncludesRequest);
        
        return new SearchResourceResponse(bundle);
    }
}
```

**Issues:**
- Adds boilerplate code
- Increases cognitive complexity
- Potential for resource leaks if `using` forgotten
- Return statements inside `using` blocks are awkward

---

### 2. **Test Changes Required** (HIGH EFFORT)

All 6 test usages must be refactored:

#### Current Test Pattern:
```csharp
var searchService = Substitute.For<ISearchService>();
var handler = new SearchResourceHandler(searchService, ...);
```

#### Required New Test Pattern:
```csharp
var searchService = Substitute.For<ISearchService>();
var searchServiceFactory = searchService.CreateMockScopeProvider();
var handler = new SearchResourceHandler(searchServiceFactory, ...);
```

**Affected Test Files:**
- `SearchResourceHandlerTests.cs` (6 test methods)
- `FhirStorageTestsFixture.cs` (integration tests)

---

### 3. **Similar Handler Changes Required** (CASCADING IMPACT)

Other handlers follow the same pattern and would need similar changes for consistency:

#### SearchResourceHistoryHandler
```csharp
public class SearchResourceHistoryHandler : IRequestHandler<...>
{
    private readonly ISearchService _searchService;  // ← Same pattern
    
    public SearchResourceHistoryHandler(ISearchService searchService, ...)
    {
        _searchService = searchService;
    }
}
```

#### SearchCompartmentHandler
```csharp
public class SearchCompartmentHandler : IRequestHandler<...>
{
    private readonly ISearchService _searchService;  // ← Same pattern
}
```

#### GetResourceHandler
```csharp
public class GetResourceHandler : BaseResourceHandler
{
    private readonly ISearchService _searchService;  // ← Same pattern
}
```

#### ResourceReferenceResolver
```csharp
public class ResourceReferenceResolver
{
    private readonly ISearchService _searchService;  // ← Same pattern
}
```

**Total Classes Requiring Changes:** At least 5-7 classes

---

### 4. **Registration Pattern Changes** (MODERATE)

#### Current Registration:
The DI container automatically handles scoped lifetime:
```csharp
// SearchResourceHandler is registered via MediatR
services.AddMediatR(typeof(SearchResourceHandler).Assembly);

// ISearchService is scoped via factory
services.AddFactory<IScoped<ISearchService>>();
```

#### Required New Registration:
Would need to explicitly register the factory:
```csharp
// Register the scope provider
services.AddSingleton<IScopeProvider<ISearchService>, ScopeProvider<ISearchService>>();

// Handler registration remains the same (MediatR handles it)
```

---

### 5. **Performance Implications**

#### Current: ✅ Optimal
- Single scope creation per HTTP request
- Automatic disposal
- No overhead

#### Proposed: ⚠️ Additional Overhead
- Manual scope creation in `Handle()` method
- Additional allocation for `IScoped<T>` wrapper
- Potential for multiple scope creations if called multiple times
- Small GC pressure increase

---

### 6. **Error Handling Complexity**

#### Current: ✅ Simple
```csharp
public async Task<SearchResourceResponse> Handle(...)
{
    var result = await _searchService.SearchAsync(...);
    return new SearchResourceResponse(bundle);
}
// Automatic cleanup even if exception thrown
```

#### Proposed: ⚠️ More Complex
```csharp
public async Task<SearchResourceResponse> Handle(...)
{
    using (var scope = _searchServiceFactory.Invoke())
    {
        try
        {
            var result = await scope.Value.SearchAsync(...);
            return new SearchResourceResponse(bundle);
        }
        catch (Exception ex)
        {
            // Exception handling with scope management
            throw;
        }
    } // Scope disposal happens here
}
```

---

## When Would This Pattern Make Sense?

The `IScopeProvider<T>` pattern is appropriate for:

### ✅ Valid Use Cases:
1. **Background Jobs** - `JobFactory` uses this pattern because jobs run outside HTTP request scope
2. **Long-running operations** - Operations that outlive a single request
3. **Lazy initialization** - When you want to defer service creation
4. **Multiple instances** - When you need to create multiple scoped instances in one request

### ❌ NOT Valid for SearchResourceHandler:
- SearchResourceHandler operates within a **single HTTP request**
- It needs **one instance** of ISearchService per request
- The lifetime aligns perfectly with **request scope**
- No need for manual lifetime management

---

## Current Pattern Analysis

### Why Current Design is Correct:

```csharp
// PersistenceModule.cs shows the intention:
services.AddFactory<IScoped<ISearchService>>();  // Factory registration
services.AddScoped<IDeletionService, DeletionService>();  // Direct registration
```

The factory (`AddFactory<IScoped<ISearchService>>`) is registered for:
- **Consumers that need manual scope control** (background jobs, bulk operations)
- **Not for request handlers** which already run in a request scope

Request handlers like `SearchResourceHandler` should use **direct injection** because:
1. They execute within HTTP request scope
2. ASP.NET Core's DI handles lifetime automatically
3. No need for manual scope management
4. Cleaner, more maintainable code

---

## Migration Effort Estimation

If you proceed with this change:

| Component | Effort | Risk |
|-----------|--------|------|
| SearchResourceHandler | 2 hours | Low |
| Unit Tests (6 tests) | 4 hours | Medium |
| Integration Tests | 2 hours | Medium |
| Similar Handlers (4+) | 8 hours | Medium |
| Code Review & Testing | 4 hours | High |
| Documentation | 2 hours | Low |
| **TOTAL** | **22 hours** | **Medium-High** |

---

## Recommendations

### Option 1: **Do NOT Make This Change** ✅ RECOMMENDED
- Current pattern is correct for request-scoped operations
- No architectural benefit
- Avoid unnecessary complexity
- Maintain consistency with ASP.NET Core DI patterns

### Option 2: If You Must Change (Not Recommended)
If there's a specific reason (e.g., you need to create multiple search service instances per request):

1. **Document the reason** clearly in ADR
2. Create helper method to reduce boilerplate:
   ```csharp
   private async Task<T> WithSearchService<T>(Func<ISearchService, Task<T>> action)
   {
       using var scope = _searchServiceFactory.Invoke();
       return await action(scope.Value);
   }
   ```
3. Update all similar handlers consistently
4. Update all tests
5. Add performance benchmarks

### Option 3: Hybrid Approach
Keep direct injection for most cases, add factory overload for special cases:
```csharp
// Constructor for normal request-scoped usage
public SearchResourceHandler(ISearchService searchService, ...)

// Constructor for special cases (testing, background operations)
public SearchResourceHandler(IScopeProvider<ISearchService> searchServiceFactory, ...)
```

---

## Conclusion

**The current architecture using direct `ISearchService` injection is correct and should be maintained.**

Changing to `IScopeProvider<ISearchService>` would:
- ❌ Add unnecessary complexity
- ❌ Require 20+ hours of refactoring
- ❌ Increase maintenance burden
- ❌ Provide no architectural benefit
- ❌ Go against ASP.NET Core DI best practices

**Recommendation:** **Reject this change** unless there is a specific, documented requirement that cannot be solved within the current architecture.

---

## Questions to Answer Before Proceeding

If you're considering this change, answer these questions:

1. **Why do you need a factory pattern?** What problem are you solving?
2. **Do you need multiple ISearchService instances per request?** (Unlikely)
3. **Is this for background operations?** (Then create a separate handler)
4. **Is this for testing purposes?** (Tests can already mock ISearchService)
5. **Is there a performance concern?** (Factory adds overhead, not improves it)

If you can't answer "yes" with strong justification to any of these, **don't make the change**.
