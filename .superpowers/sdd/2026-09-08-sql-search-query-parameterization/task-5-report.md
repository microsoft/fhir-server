# Task 5 Report

## Status
Completed.

## Files
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/ParserUtil.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IdSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/LastUpdatedSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/SortSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/RevIncludeSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ReversedChainSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/ParserUtilTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SearchParameterSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/IdSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/LastUpdatedSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/SortSqlParserTests.cs`

## Red Evidence
Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdSqlParserTests|FullyQualifiedName~LastUpdatedSqlParserTests|FullyQualifiedName~SortSqlParserTests|FullyQualifiedName~IncludeSqlParserTests|FullyQualifiedName~ParserUtilTests"
```

Representative failures before implementation:

- `IdSqlParserTests.GivenSingleId_WhenParse_ThenUsesTypedParameterExcludedFromHash`
  - Expected substring: `r.ResourceId = @p0`
  - Actual SQL still contained raw `_id` literals.
- `IncludeSqlParserTests.GivenInclude_WhenParse_ThenUsesParameterizedTopThresholdAndRowLimitPolicies`
  - Expected substring: `DISTINCT TOP (@p0)`
  - Actual SQL started with `SELECT DISTINCT TOP 7`.
- `ParserUtilTests.GivenContinuationToken_WhenAddFirstCteFilters_ThenUsesExcludedPagingParameters`
  - Expected substring: `r.ResourceSurrogateId > @p0`
  - Actual SQL still interpolated the continuation surrogate ID.
- `SortSqlParserTests.GivenStringSortContinuation_WhenCreateSortCte_ThenUsesSeparateTypedParametersExcludedFromHash`
  - Expected the new `CreateSortCte(..., ParserOptions options, ...)` policy path
  - Actual implementation did not yet expose that overload or bind separate continuation parameters.

Exact summary:
- `Test run summary: Failed!`
- `total: 39`
- `failed: 10`
- `succeeded: 29`
- `skipped: 0`
- `duration: 3s 538ms`

## Green Evidence
Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdSqlParserTests|FullyQualifiedName~LastUpdatedSqlParserTests|FullyQualifiedName~SortSqlParserTests|FullyQualifiedName~IncludeSqlParserTests|FullyQualifiedName~ParserUtilTests"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (950ms)`
- `Test run summary: Passed!`
- `total: 39`
- `failed: 0`
- `succeeded: 39`
- `skipped: 0`
- `duration: 1s 317ms`

Full parser-suite command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (995ms)`
- `Test run summary: Passed!`
- `total: 171`
- `failed: 0`
- `succeeded: 171`
- `skipped: 0`
- `duration: 1s 352ms`

## Self-Review
- `_id` now binds through `VLatest.Resource.ResourceId` with `includeInHash: false`, and touched tests assert no raw request IDs in SQL.
- Count and include `TOP` limits now emit `TOP (@pN)` with untyped parameters excluded from the hash.
- Include partial-threshold / row-limit predicates now use separate parameters with hash inclusion, covering `_include` and `_revinclude`.
- Sort continuation now threads `ParserOptions` through `CreateSortCte(...)`, binds typed continuation values, and forces separate equality/range parameters even for identical sort values.
- Continuation paging values in `ParserUtil`, reverse-chain, and the touched chained caller path now bind surrogate IDs as parameters while keeping resource-type/search-parameter IDs literal via manager policy.
- Scoped search of touched production files found no remaining request-derived literal interpolation patterns.
- Scoped diff review via `pr-review-toolkit:code-reviewer` returned `no findings`.

## Concerns
- None.

## Fix Round 1 Evidence
- Fixed legacy one-part continuation handling in `ParserUtil.cs` by preserving surrogate-only paging when `ContinuationToken.ResourceTypeId` is absent.
- Removed duplicate reverse-chain continuation predicates from `ReversedChainSqlParser.cs` and grouped `_has` handling in `SearchParameterSqlParser.cs` so those paths now inherit the shared `ParserUtil` continuation semantics, including `_lastUpdated` surrogate-only paging.
- Added focused assertions for legacy continuation, typed include/revinclude tie-breakers, grouped/non-grouped reverse-chain continuations, generic DateTime sort continuation parameters, and reverse-chain `_lastUpdated` continuation behavior.

Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ParserUtilTests|FullyQualifiedName~IncludeSqlParserTests|FullyQualifiedName~SortSqlParserTests|FullyQualifiedName~SearchParameterSqlParserTests"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (1s 109ms)`
- `Test run summary: Passed!`
- `total: 29`
- `failed: 0`
- `succeeded: 29`
- `skipped: 0`
- `duration: 1s 716ms`

Full parser-suite command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (1s 077ms)`
- `Test run summary: Passed!`
- `total: 177`
- `failed: 0`
- `succeeded: 177`
- `skipped: 0`
- `duration: 1s 496ms`

Review evidence:
- `pr-review-toolkit:code-simplifier` made small readability-only updates in `ParserUtil.cs` and `ReversedChainSqlParser.cs`; focused tests remained green afterward.
- Final `pr-review-toolkit:code-reviewer` pass returned `No findings.`
