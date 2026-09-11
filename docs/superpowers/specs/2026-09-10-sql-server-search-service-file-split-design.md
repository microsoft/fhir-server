# SQL Server Search Service File Split Design

## Context

`SqlServerSearchService.cs` has grown to approximately 2,856 lines and 158 KB. It now contains several distinct responsibilities:

- Search request orchestration, including two-phase sort and include behavior.
- SQL command generation and result materialization.
- Include continuation execution.
- Long-running-query and Query Store diagnostics.
- Query Store concurrency limiting and circuit breaking.
- Surrogate ID range APIs.
- Search-parameter statistics support.
- Large blocks of retired expression-tree and optimization code.

The concentration of these responsibilities makes the service difficult to navigate and increases merge-conflict risk. The goal is to divide the implementation into cohesive files without changing runtime behavior, dependency injection, public or internal APIs, exception behavior, or SQL generation.

## Decision

Convert `SqlServerSearchService` to an `internal partial class` and organize its implementation into responsibility-based files. This is a file-level decomposition only; it does not introduce new injected collaborators.

### File Boundaries

#### `SqlServerSearchService.cs`

Retains the service identity and request-level orchestration:

- Shared constructor dependencies and constructor validation.
- Shared constants and properties that do not belong exclusively to another responsibility.
- `SearchAsync`.
- Two-phase sort and include orchestration.
- Accurate total-count orchestration.
- `IsValidResourceType`.

Expected size: approximately 450 lines.

#### `SqlServerSearchService.Execution.cs`

Owns normal search execution:

- `RunSearch`.
- `SearchImpl`.
- Search SQL command setup and custom-query selection.
- Search result row materialization.
- Match/include separation and continuation-token construction.
- Query-hint command population used by the normal execution path.
- `ReadWrapper`.
- Debug SQL command logging.

Expected size: approximately 650 lines.

#### `SqlServerSearchService.Include.cs`

Owns include-continuation execution:

- `SearchIncludeImpl`.
- Include-page row materialization.
- Include continuation-token construction.

Expected size: approximately 220 lines.

#### `SqlServerSearchService.QueryStore.cs`

Owns long-running-query diagnostics:

- Long-running-query parameter IDs and cached parameter state.
- Query Store lookup limits, semaphore, and circuit-breaker state.
- Query normalization, fragment splitting, whitespace stripping, schema-prefix stripping, and parameter-hash extraction.
- Fire-and-forget lookup dispatch.
- Circuit-breaker transitions.
- Query Store SQL execution and result formatting.

Expected size: approximately 650 lines.

#### `SqlServerSearchService.SurrogateRanges.cs`

Owns administrative range operations:

- `SearchBySurrogateIdRange` overloads.
- Surrogate-range command creation and timeout selection.
- `GetSurrogateIdRanges`.
- `GetUsedResourceTypes`.

Expected size: approximately 180 lines.

#### `SqlServerSearchService.Statistics.cs`

Owns search-parameter statistics support:

- Statistics feature-flag state.
- `GetKeyColumns`.
- Statistics cache access and reset hooks.
- Database statistics reads.
- `ResourceSearchParamStats`.
- The dormant statistics creation and expression-walking implementation.

The dormant statistics implementation will remain commented and will be moved intact because it is planned for future reactivation. This refactor will neither re-enable nor redesign it.

Expected active size: approximately 100 lines, plus the preserved dormant implementation.

## Cleanup Scope

Before moving active code, remove disabled code that is unrelated to the planned statistics work:

- The retired expression-tree search generation pipeline.
- Disabled token-search stored-procedure optimization logic.
- Disabled direct ID optimization logic.
- Disabled nested token and table-valued-parameter helpers used only by those optimizations.

Remove imports that become unused after this cleanup.

Do not remove or modify the dormant statistics implementation.

## Dependency and State Rules

- The constructor signature and registration remain unchanged.
- Existing fields retain their types and initialization behavior.
- Responsibility-specific static state may move to its owning partial file.
- Shared state remains in the primary file when assigning it to one partial would obscure ownership.
- No new interfaces, services, factories, or dependency-injection registrations are introduced.
- No method signature or visibility changes are required solely to enable the split; partial-class members can continue calling each other directly.
- Existing static test hooks remain available under `SqlServerSearchService`.

## Behavior Preservation

The split must not change:

- Generated SQL or SQL parameters.
- Query-plan reuse or custom-query selection.
- Search paging, sort, include, reverse-include, or SMART behavior.
- Continuation-token contents.
- Query Store lookup behavior, concurrency limits, or circuit-breaker semantics.
- Surrogate-range behavior.
- Statistics reads, feature-flag behavior, or the disabled state of statistics creation.
- Logging levels, event names, exception types, or cancellation flow.

Mechanical moves should preserve method bodies. Any required code change beyond namespace imports and partial declarations must be isolated and justified separately.

## Test Organization

Existing focused test suites already align with several boundaries:

- `SqlServerSearchServiceTests` covers construction, common helpers, statistics hooks, and date semantics.
- `SqlServerSearchServiceQueryStoreTests` covers Query Store normalization and diagnostics.
- `SqlServerSearchServiceCircuitBreakerTests` covers Query Store circuit breaking.
- `SqlServerSearchServiceOptimizationTests` covers retained optimization qualification behavior.
- Integration and E2E search tests cover execution, paging, include, SMART, and range behavior.

Tests do not need to move merely because production code moves. Test files should be renamed or split only when doing so improves ownership without changing their assertions.

## Implementation Sequence

1. Record the current build and focused/full test baseline.
2. Remove only the approved retired code blocks and unused imports.
3. Introduce the partial class and extract Query Store diagnostics.
4. Extract surrogate-range operations.
5. Extract statistics support, moving dormant statistics code intact.
6. Extract include execution.
7. Extract normal SQL execution and result materialization.
8. Leave request-level orchestration and shared construction in the primary file.
9. Run focused validation after each move.
10. Run the SQL Server build, complete SQL Server unit tests, and representative search E2E tests after the final split.

Each extraction should be independently reviewable and keep the repository buildable.

## Validation

For each responsibility move:

- Build `Microsoft.Health.Fhir.SqlServer`.
- Run the focused unit tests associated with the moved responsibility.
- Inspect the diff for method-body changes rather than accepting a move based only on compilation.

After all moves:

- Run all `SqlServerSearchService` unit-test classes.
- Run the complete SQL Server unit-test project and compare failures with the recorded baseline.
- Run representative search, compartment, include, sort, and Member Match E2E coverage when the required test infrastructure is available.
- Confirm no testhost or server process started by validation remains running.

## Consequences

### Benefits

- Each file has a clear responsibility and can be reviewed independently.
- Query Store diagnostic work no longer obscures the search execution path.
- Range and statistics operations are easier to discover.
- Future statistics reactivation has an explicit home.
- Merge conflicts should decrease because unrelated changes land in different files.
- The behavior-preserving partial split avoids dependency-injection churn.

### Limitations

- The class remains a single runtime type with broad dependencies.
- Partial files can still access all private state, so compiler-enforced responsibility boundaries are not introduced.
- The preserved dormant statistics implementation remains a large commented block.
- Extracting collaborators for Query Store diagnostics or result materialization is deferred to a separate design and change.
