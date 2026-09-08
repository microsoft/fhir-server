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
