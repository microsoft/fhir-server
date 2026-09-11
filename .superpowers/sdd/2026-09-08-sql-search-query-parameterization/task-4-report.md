# Task 4 Report

## Status
Completed.

## Files
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/ReferenceSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/ReferenceSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/CompositeParsers/CompositeParserTests.cs`

## Red Evidence
Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ReferenceSqlParserTests|FullyQualifiedName~CompositeParserTests"
```

Representative failures before implementation:

- `ReferenceSqlParserTests.GivenIdOnly_WhenBuildWhereClause_ThenGeneratesReferenceIdConditionOnly`
  - Expected: `t.ReferenceResourceId = @p0`
  - Actual: `t.ReferenceResourceId = '123'`
- `CompositeParserTests.GivenReferenceTokenComposite_WhenBuildWhereClause_ThenCombinesReferenceAndTokenConditions`
  - Expected substring: `ReferenceResourceId1 = @p0`
  - Actual SQL started with: `(t.ReferenceResourceId1 = 'reference-id' ...`

Exact summary:
- `Test run summary: Failed!`
- `total: 17`
- `failed: 7`
- `succeeded: 10`
- `skipped: 0`
- `duration: 1s 635ms`

## Green Evidence
Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ReferenceSqlParserTests|FullyQualifiedName~CompositeParserTests"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (946ms)`
- `Test run summary: Passed!`
- `total: 17`
- `failed: 0`
- `succeeded: 17`
- `skipped: 0`
- `duration: 1s 548ms`

Full parser-suite command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (988ms)`
- `Test run summary: Passed!`
- `total: 159`
- `failed: 0`
- `succeeded: 159`
- `skipped: 0`
- `duration: 1s 344ms`

## Self-Review
- Parameterized `ReferenceResourceId` and `BaseUri` through `ParserOptions.AddParameter(...)` with `VLatest.ReferenceSearchParam` metadata and `includeInHash: true`.
- Routed `ReferenceResourceTypeId` through the same request manager/metadata path so manager policy keeps it as a numeric SQL literal.
- Preserved existing reference parsing behavior, including structural IDs and modifier precedence.
- Added composite assertions that verify shared `ParserOptions`, parameter ordering, and absence of raw component input in emitted SQL.
- Added a `GetSearchJoinInfo(...)` composite test to verify combined reverse-chain leaf predicate generation reuses the request-scoped manager rather than a fresh/default context.

## Concerns
- None.

## Fix round 1 evidence

- Added `SearchParameterSqlParserTests.GivenMultipleReverseChainEntriesInSameAndGroup_WhenParseMultiple_ThenUsesCombinedCteAndSharedCommandParameters` to exercise the real combined reverse-chain path through `ParseMultiple(...)` with multiple `_has:` entries in one AND group, asserting shared request-scoped parameterization, placeholder-only SQL, and deterministic leaf/component parameter order.
- Added `ReferenceSqlParserTests.GivenRelativeReferenceAndConflictingTypeModifier_WhenBuildWhereClause_ThenValueTypeTakesPrecedence` to lock explicit value resource-type precedence over a conflicting modifier while confirming the resource ID stays parameterized.

Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ReferenceSqlParserTests|FullyQualifiedName~CompositeParserTests|FullyQualifiedName~SearchParameterSqlParserTests"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (591ms)`
- `Test run summary: Passed!`
- `total: 19`
- `failed: 0`
- `succeeded: 19`
- `skipped: 0`
- `duration: 977ms`

Full parser-suite command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (931ms)`
- `Test run summary: Passed!`
- `total: 161`
- `failed: 0`
- `succeeded: 161`
- `skipped: 0`
- `duration: 1s 291ms`

## Fix round 2

- Added a precise assertion to `SearchParameterSqlParserTests.GivenMultipleReverseChainEntriesInSameAndGroup_WhenParseMultiple_ThenUsesCombinedCteAndSharedCommandParameters` that verifies `ParseMultiple(...)` emits the top-level `refTarget.ResourceTypeId IN (...)` Patient scope filter using the resolved model id.

Focused command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SearchParameterSqlParserTests.GivenMultipleReverseChainEntriesInSameAndGroup_WhenParseMultiple_ThenUsesCombinedCteAndSharedCommandParameters"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (928ms)`
- `Test run summary: Passed!`
- `total: 1`
- `failed: 0`
- `succeeded: 1`
- `skipped: 0`
- `duration: 1s 495ms`

Full parser-suite command:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Exact summary:
- `C:\Users\rojo\source\repos\copilot-worktrees\fhir-server\rojo-microsoft-miniature-disco\src\Microsoft.Health.Fhir.SqlServer.UnitTests\bin\Debug\net10.0\Microsoft.Health.Fhir.SqlServer.UnitTests.dll (net10.0|x64) passed (917ms)`
- `Test run summary: Passed!`
- `total: 161`
- `failed: 0`
- `succeeded: 161`
- `skipped: 0`
- `duration: 1s 275ms`
