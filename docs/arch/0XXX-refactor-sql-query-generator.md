# ADR 0XXX: Refactor SqlQueryGenerator for Improved Maintainability

## Status

Proposed

## Context

The existing `SqlQueryGenerator` class has grown to over 1,900 lines of code with complex state management, making it difficult to:

1. **Debug** - Tracing through deeply nested logic and tracking state across multiple mutable fields is challenging
2. **Test** - The monolithic design makes unit testing individual components nearly impossible without running the entire query generation process
3. **Maintain** - Understanding and modifying specific functionality requires deep knowledge of the entire class
4. **Extend** - Adding new search parameter types or expression kinds requires modifications to large switch statements and careful state management

### Current Issues

- **Monolithic Class**: Single 1,934-line class handling all query generation concerns
- **State Management**: 20+ mutable fields tracking various aspects of query generation (`_tableExpressionCounter`, `_sortVisited`, `_unionVisited`, `_smartV2UnionVisited`, etc.)
- **Mixed Concerns**: SQL generation, expression visiting, business logic, and state tracking intertwined
- **Large Methods**: Individual methods (`VisitSqlRoot`) spanning hundreds of lines
- **Switch Complexity**: Large switch statements with 12+ cases in `VisitTable`
- **Difficult Testing**: Cannot test individual CTE generation strategies in isolation
- **Poor Discoverability**: Hard to locate where specific SQL patterns are generated

### Example of Current Complexity

```csharp
// Current: All state scattered across class
private int _tableExpressionCounter = -1;
private int _smartv2ScopeUnionCTE = -1;
private bool _sortVisited = false;
private bool _unionVisited = false;
private bool _smartV2UnionVisited = false;
private bool _firstChainAfterUnionVisited = false;
// ... 15+ more fields

// Current: Large switch statement
switch (searchParamTableExpression.Kind)
{
    case SearchParamTableExpressionKind.Normal:
        // 100+ lines
        break;
    case SearchParamTableExpressionKind.Union:
        // 80+ lines
        break;
    // ... 10+ more cases
}
```

## Decision

We will refactor `SqlQueryGenerator` using the **Strategy**, **Builder**, and **State Object** patterns to create a more maintainable architecture while preserving all existing functionality.

### Architecture Overview

```
RefactoredSqlQueryGenerator (Orchestrator)
├── QueryGenerationContext (Centralized State)
├── CteGeneratorFactory (Strategy Selection)
│   ├── NormalCteGenerator
│   ├── UnionCteGenerator
│   ├── ChainCteGenerator
│   ├── IncludeCteGenerator
│   ├── SortCteGenerator
│   ├── TopCteGenerator
│   └── NotExistsCteGenerator
├── SelectClauseBuilder (SELECT clause construction)
├── ParameterHashBuilder (Parameter hash generation)
└── Helpers
    ├── CteJoinHelper (CTE join logic)
    └── SortingHelper (ORDER BY logic)
```

### Key Components

#### 1. QueryGenerationContext
**Purpose**: Centralized state container replacing scattered fields

```csharp
internal class QueryGenerationContext
{
    // All state in one place
    public int CurrentCteIndex { get; set; }
    public bool SortVisited { get; set; }
    public bool UnionVisited { get; set; }
    // ... all other state

    // Helper methods
    public string GetNextCteTableName()
    public string GetCurrentCteTableName()
}
```

**Benefits**:
- Single source of truth for all state
- Easy to pass to helper methods
- Simplifies testing with mock contexts

#### 2. ICteGenerator (Strategy Pattern)
**Purpose**: Separate implementation for each expression kind

```csharp
internal interface ICteGenerator
{
    bool CanGenerate(SearchParamTableExpressionKind kind);
    void Generate(SearchParamTableExpression expression, QueryGenerationContext context);
}
```

**Implementations**:
- `NormalCteGenerator` - Standard search parameter filtering
- `UnionCteGenerator` - Compartment and UNION operations
- `ChainCteGenerator` - Forward and reverse chaining
- `IncludeCteGenerator` - _include and _revinclude
- `SortCteGenerator` - Sort operations with SortValue
- `TopCteGenerator` - TOP/pagination logic
- `NotExistsCteGenerator` - Missing parameter (NOT EXISTS)

**Benefits**:
- Each generator is ~50-150 lines (vs 1,900+ line monolith)
- Can test each generator independently
- Easy to add new expression kinds
- Clear separation of concerns

#### 3. Clause Builders
**Purpose**: Reusable components for common SQL clauses

- `HistoryClauseBuilder` - WHERE clause for history filtering
- `DeletedClauseBuilder` - WHERE clause for soft deletes
- `SelectClauseBuilder` - SELECT column list
- `ParameterHashBuilder` - Parameter hash comments

**Benefits**:
- DRY (Don't Repeat Yourself)
- Consistent clause generation
- Easy to modify clause logic in one place

#### 4. Helpers
**Purpose**: Shared logic across generators

- `CteJoinHelper` - Logic for joining CTEs, determining JOIN vs EXISTS
- `SortingHelper` - ORDER BY clause construction, sort detection

### Migration Strategy

1. **Phase 1: Create Refactored Classes** ✅
   - Implement all new classes alongside existing code
   - No changes to existing functionality

2. **Phase 2: Feature Flag** (Next)
   - Add configuration option to switch implementations
   - Default to existing `SqlQueryGenerator`

3. **Phase 3: Testing & Validation**
   - Comprehensive integration tests
   - Side-by-side SQL comparison tests
   - Performance benchmarking

4. **Phase 4: Gradual Rollout**
   - Enable for specific search scenarios
   - Monitor for regressions
   - Iterate based on findings

5. **Phase 5: Complete Migration**
   - Make refactored version default
   - Deprecate original implementation
   - Remove old code after validation period

## Consequences

### Positive

1. **Improved Debuggability**
   - Smaller, focused methods are easier to step through
   - State is centralized and visible
   - Clear responsibility boundaries

2. **Enhanced Testability**
   - Each generator can be unit tested independently
   - Mock contexts simplify test setup
   - Test specific scenarios without full integration

3. **Better Maintainability**
   - Changes to one expression kind don't affect others
   - Clear file organization by responsibility
   - Easier onboarding for new developers

4. **Easier Extension**
   - New expression kinds: implement `ICteGenerator`
   - New clauses: create new builder
   - Modify logic: change specific generator

5. **Preserved Functionality**
   - All existing SQL patterns maintained
   - Same output queries
   - No behavior changes

### Negative

1. **More Files**
   - ~15 new files vs 1 large file
   - Mitigated by: Clear organization in /Refactored folder structure

2. **Learning Curve**
   - Developers must understand new architecture
   - Mitigated by: Comprehensive documentation, clear naming

3. **Migration Risk**
   - Bugs during transition
   - Mitigated by: Feature flag, extensive testing, gradual rollout

4. **Temporary Duplication**
   - Both implementations exist during migration
   - Mitigated by: Time-boxed migration, clear deprecation plan

### Neutral

1. **Performance**
   - Slightly more object allocation (minimal)
   - Same SQL output means same query plans
   - No measurable performance difference expected

2. **Code Volume**
   - Similar total lines of code
   - Distributed across multiple focused files

## Implementation Notes

### Incomplete Components

The following generators require full implementation with logic from the original:

1. `ChainCteGenerator` - Complex chaining logic
2. `IncludeCteGenerator` - Include/revinclude operations
3. `SortCteGenerator` - Sort with continuation tokens

These are currently placeholders throwing `NotImplementedException`.

### Testing Strategy

1. **Unit Tests** - Test each generator with mock contexts
2. **Integration Tests** - Compare SQL output with original
3. **E2E Tests** - Run existing test suite with feature flag enabled
4. **Performance Tests** - Benchmark query generation time

### Example Test

```csharp
[Fact]
public void NormalCteGenerator_GeneratesCorrectSql()
{
    // Arrange
    var context = CreateMockContext();
    var generator = new NormalCteGenerator(historyBuilder, joinHelper);
    var expression = CreateNormalExpression();

    // Act
    generator.Generate(expression, context);

    // Assert
    var sql = context.StringBuilder.ToString();
    Assert.Contains("SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1", sql);
}
```

## Alternatives Considered

### 1. Partial Refactoring (Extract Methods)
**Rejected**: Doesn't address core state management issues

### 2. Complete Rewrite
**Rejected**: Too risky, loses institutional knowledge embedded in original

### 3. Template Method Pattern
**Rejected**: Still results in large base class, doesn't solve testability

### 4. Chosen: Strategy + Builder + State Object
**Selected**: Best balance of maintainability, testability, and risk

## References

- Original Issue: [Link to issue]
- Design Patterns Used:
  - Strategy Pattern: Gang of Four
  - Builder Pattern: Gang of Four
  - State Object Pattern: Martin Fowler, Refactoring

## Future Considerations

1. **Query Plan Caching**: Consider caching generated CTE strategies
2. **SQL Optimization**: Use builder pattern to apply SQL optimizations
3. **Additional Generators**: Support for custom expression types
4. **Metrics**: Add telemetry to track generator usage patterns
