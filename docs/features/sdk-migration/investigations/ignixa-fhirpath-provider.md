# Investigation: Ignixa FHIRPath Provider Migration

**Feature**: sdk-migration
**Status**: In Progress
**Created**: 2025-12-31

## Approach

Replace Firely's `FhirPathCompiler` and `CompiledExpression` with Ignixa's native FHIRPath engine (`FhirPathParser`, `FhirPathEvaluator`, `FhirPathDelegateCompiler`) to:

1. **Eliminate ITypedElement shim overhead** - Currently `IgnixaResourceElement.ToTypedElement()` converts to Firely's interface for FhirPath evaluation
2. **Use optimized delegate compilation** - Ignixa compiles ~80% of common search patterns to direct delegates
3. **Native IElement evaluation** - Work directly with Ignixa's `IElement` without conversion layers

### Current Architecture (Firely-based)

```
TypedElementSearchIndexer
    ├─ static FhirPathCompiler _compiler
    ├─ ConcurrentDictionary<string, CompiledExpression> _expressions
    │
    └─ Extract(ResourceElement resource)
         ├─ context = _modelInfoProvider.GetEvaluationContext(...)
         ├─ context.Resource = resource.Instance  (ITypedElement)
         │
         └─ foreach searchParameter:
              ├─ expression = _expressions.GetOrAdd(expr, _compiler.Compile)
              └─ results = expression.Invoke(resource.Instance, context)
                           └─ returns IEnumerable<ITypedElement>
```

### Proposed Architecture (Ignixa-based)

```
TypedElementSearchIndexer (updated)
    ├─ IFhirPathProvider _fhirPathProvider  (injected)
    │
    └─ Extract(ResourceElement resource)
         ├─ context = new FhirEvaluationContext { Resource = element }
         │
         └─ foreach searchParameter:
              ├─ compiledExpr = _fhirPathProvider.Compile(expr)
              └─ results = compiledExpr.Invoke(element, context)
                           └─ returns IEnumerable<IElement>
                           └─ adapter converts to ITypedElement if needed
```

### Key Components to Create

| Component | Purpose |
|-----------|---------|
| `IFhirPathProvider` | Injectable interface for FhirPath compilation/evaluation |
| `IgnixaFhirPathProvider` | Implementation using Ignixa's parser/evaluator/compiler |
| `ICompiledFhirPath` | Wrapper for compiled expression (delegate or interpreted) |
| `FhirPathEvaluationContext` | Adapter between Firely's `EvaluationContext` and Ignixa's |

## Tradeoffs

| Pros | Cons |
|------|------|
| Native IElement evaluation - no shim overhead | Requires adapter for ITypedElement-expecting code |
| Delegate compilation for 80% of patterns | Some expressions fall back to interpreter |
| Single parser/evaluator for all FHIR versions | Different from Firely's FhirPath behavior in edge cases |
| Built-in caching in delegate compiler | Need to implement expression caching layer |
| Supports %resource, %rootResource variables | Resolve() function needs element resolver setup |
| Schema-aware type checking | Requires ISchema in evaluation context |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
- [x] F5 Developer Experience (works with minimal setup)
- [x] FHIR spec compliance (Ignixa implements FHIRPath 2.0)
- [x] Consistent with existing patterns (injectable provider)

## Evidence

### Current FHIRPath Usage Analysis

Based on codebase exploration:

**1. TypedElementSearchIndexer** (PRIMARY - search indexing)
- Location: `src/Microsoft.Health.Fhir.Core/Features/Search/TypedElementSearchIndexer.cs`
- Uses: `FhirPathCompiler`, `CompiledExpression`, `EvaluationContext`
- Pattern: Compile once, invoke many times per resource
- Critical: Must handle all search parameter expressions

**2. ResourceElement.Scalar/Select/Predicate** (GENERAL - FhirPath on resources)
- Location: `src/Microsoft.Health.Fhir.Core/Models/ResourceElement.cs`
- Uses: `ITypedElement.Scalar()`, `ITypedElement.Select()`, `ITypedElement.Predicate()`
- Pattern: Ad-hoc FhirPath evaluation on resource elements

**3. IgnixaResourceElement** (CURRENT - bridges to Firely)
- Location: `src/Microsoft.Health.Fhir.Ignixa/IgnixaResourceElement.cs`
- Currently: Converts to ITypedElement via `Element.ToTypedElement()` for FhirPath
- Opportunity: Use Ignixa FhirPath directly on IElement

**4. Soft Delete Extension** (SPECIFIC - predicate check)
- Location: `src/Microsoft.Health.Fhir.Ignixa/IgnixaResourceElementExtensions.cs`
- Uses: `resourceElement.Predicate(KnownFhirPaths.IsSoftDeletedExtension)`

### Ignixa FHIRPath Capabilities

**Parser** (`Ignixa.FhirPath.Parser.FhirPathParser`):
- Superpower-based parser
- Returns `Expression` AST
- Supports trivia preservation for round-tripping

**Evaluator** (`Ignixa.FhirPath.Evaluation.FhirPathEvaluator`):
- Full FHIRPath 2.0 support
- Works directly with `IElement`
- Supports all operators: `=`, `!=`, `~`, `!~`, `>`, `>=`, `<`, `<=`, `and`, `or`, `xor`, `implies`, `in`, `contains`, `is`, `as`
- FHIR functions: `extension()`, `resolve()`, `getResourceKey()`, `getReferenceKey()`

**Delegate Compiler** (`Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler`):
- Compiles ~80% of common patterns to delegates
- Coverage:
  - Simple paths (30%): `"name"`, `"identifier"`
  - Two-level paths (40%): `"name.family"`, `"identifier.value"`
  - Where clauses (15%): `"telecom.where(system='phone')"`
  - Functions (10%): `"name.first()"`, `"identifier.exists()"`
- Falls back to interpreter for complex expressions

**Evaluation Context** (`Ignixa.FhirPath.Evaluation.EvaluationContext`):
- Environment variables dictionary
- `Resource` and `RootResource` properties
- `GetEnvironmentVariable()` / `SetEnvironmentVariable()` methods

### Comparison: Firely vs Ignixa FhirPath

| Feature | Firely | Ignixa |
|---------|--------|--------|
| Input type | `ITypedElement` | `IElement` |
| Output type | `IEnumerable<ITypedElement>` | `IEnumerable<IElement>` |
| Compilation | `FhirPathCompiler.Compile()` | `FhirPathParser.Parse()` |
| Execution | `CompiledExpression.Invoke()` | `FhirPathEvaluator.Evaluate()` |
| Optimization | None (interpreted) | Delegate compilation for common patterns |
| Context | `EvaluationContext` | `EvaluationContext` / `FhirEvaluationContext` |
| resolve() support | Via resolver delegate | Via `FhirEvaluationContext.ElementResolver` |

### Key Finding: IgnixaResourceElement Already Has Ignixa FhirPath Setup

Current `IgnixaResourceElement` methods delegate to Firely via shim:

```csharp
public T? Scalar<T>(string fhirPath)
{
    var typedElement = ToTypedElement();  // <-- Conversion to Firely
    var result = typedElement.Scalar(fhirPath);  // <-- Firely FhirPath
    return result is T typedResult ? typedResult : default;
}
```

Could become (direct Ignixa):

```csharp
public T? Scalar<T>(string fhirPath)
{
    var evaluator = new FhirPathEvaluator();
    var expression = _parser.Parse(fhirPath);
    var results = evaluator.Evaluate(Element, expression);
    return results.FirstOrDefault()?.Value is T value ? value : default;
}
```

## Implementation Plan

### Phase 1: Create Injectable Interface

```csharp
// IFhirPathProvider.cs
public interface IFhirPathProvider
{
    ICompiledFhirPath Compile(string expression);
}

// ICompiledFhirPath.cs
public interface ICompiledFhirPath
{
    IEnumerable<IElement> Evaluate(IElement input, FhirPathContext? context = null);
    IEnumerable<ITypedElement> EvaluateTyped(ITypedElement input, EvaluationContext? context = null);
}
```

### Phase 2: Implement Ignixa Provider

```csharp
// IgnixaFhirPathProvider.cs
public class IgnixaFhirPathProvider : IFhirPathProvider
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathDelegateCompiler _delegateCompiler;
    private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache = new();

    public IgnixaFhirPathProvider()
    {
        _delegateCompiler = new FhirPathDelegateCompiler(_evaluator);
    }

    public ICompiledFhirPath Compile(string expression)
    {
        return _cache.GetOrAdd(expression, expr =>
        {
            var ast = _parser.Parse(expr);
            var compiledDelegate = _delegateCompiler.TryCompile(ast);
            return new CompiledFhirPath(ast, compiledDelegate, _evaluator);
        });
    }
}
```

### Phase 3: Update Search Indexer

```csharp
// TypedElementSearchIndexer.cs - updated
public class TypedElementSearchIndexer : ISearchIndexer
{
    private readonly IFhirPathProvider _fhirPathProvider;  // NEW
    // Remove: private static readonly FhirPathCompiler _compiler = new();
    // Remove: private readonly ConcurrentDictionary<string, CompiledExpression> _expressions = new();

    public TypedElementSearchIndexer(
        IFhirPathProvider fhirPathProvider,  // NEW
        ISupportedSearchParameterDefinitionManager searchParameterDefinitionManager,
        ...)
    {
        _fhirPathProvider = fhirPathProvider;
        ...
    }

    private List<ISearchValue> ExtractSearchValues(...)
    {
        var compiledExpr = _fhirPathProvider.Compile(fhirPathExpression);

        // For resources that come as ITypedElement (Firely)
        IEnumerable<ITypedElement> extractedValues = compiledExpr.EvaluateTyped(element, context);

        // For resources that come as IElement (Ignixa) - faster path
        // IEnumerable<IElement> extractedValues = compiledExpr.Evaluate(element, context);
        ...
    }
}
```

### Phase 4: Update IgnixaResourceElement

```csharp
// IgnixaResourceElement.cs - use native Ignixa FhirPath
public class IgnixaResourceElement : IResourceElement
{
    private readonly IFhirPathProvider _fhirPathProvider;  // Injected or static

    public T? Scalar<T>(string fhirPath)
    {
        var compiled = _fhirPathProvider.Compile(fhirPath);
        var results = compiled.Evaluate(Element);
        return results.FirstOrDefault()?.Value is T value ? value : default;
    }
}
```

## Alternative Approaches

### 1. Keep Firely FhirPath (Status Quo)
Continue using `ToTypedElement()` shim for FhirPath evaluation.
- **Pro:** No changes needed, proven stability
- **Con:** Performance overhead from shim conversion

### 2. Dual-Mode Provider
Provider that tries Ignixa first, falls back to Firely for unsupported expressions.
- **Pro:** Maximum compatibility
- **Con:** Complexity, two code paths to maintain

### 3. Direct Evaluator Integration
Skip injectable interface, embed Ignixa evaluator directly in search indexer.
- **Pro:** Simpler implementation
- **Con:** Less testable, harder to swap implementations

## Verdict

**Recommended: Implement Phase 1-4** with the injectable `IFhirPathProvider` approach.

Key reasons:
1. **Performance**: Delegate compilation provides significant speedup for search indexing
2. **Consistency**: Single FhirPath implementation across all code paths
3. **Testability**: Injectable interface enables unit testing with mocks
4. **Gradual migration**: Can run both providers in parallel during transition

### Success Criteria

1. [ ] Search indexing works correctly with Ignixa FhirPath
2. [ ] All search parameter expressions evaluate correctly
3. [ ] Performance equal or better than Firely FhirPath
4. [ ] `resolve()` function works for reference resolution
5. [ ] `%resource` variable works in expressions
6. [ ] Unit tests pass for all FhirPath usage patterns

### Estimated Effort

- Phase 1 (Interface): 2-4 hours
- Phase 2 (Provider): 4-8 hours
- Phase 3 (Search Indexer): 8-16 hours
- Phase 4 (IgnixaResourceElement): 2-4 hours
- Testing: 8-16 hours
- **Total**: 24-48 hours (3-6 days)

## Next Steps

1. [ ] Create `IFhirPathProvider` interface in `Microsoft.Health.Fhir.Core`
2. [ ] Create `IgnixaFhirPathProvider` in `Microsoft.Health.Fhir.Ignixa`
3. [ ] Register provider in DI container
4. [ ] Update `TypedElementSearchIndexer` to use provider
5. [ ] Update `IgnixaResourceElement` to use native Ignixa FhirPath
6. [ ] Add unit tests for FhirPath provider
7. [ ] Benchmark performance comparison
