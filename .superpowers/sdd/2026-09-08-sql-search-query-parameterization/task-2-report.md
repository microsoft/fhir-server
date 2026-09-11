# Task 2 Report

## Status
Completed.

## Files changed
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/TokenSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/StringSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/UriSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/TokenSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/StringSqlParserTests.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/UriSqlParserTests.cs`

## Implementation details
- Parameterized token code/system/text predicates through `ParserOptions.AddParameter(...)`.
- Parameterized string prefix/exact/contains predicates and preserved overflow-column selection.
- Parameterized URI exact/above/below predicates and removed raw SQL value embedding.
- Updated unit tests to use command-backed parser options and assert parameter names/values instead of literals.

## Test commands and outcomes
- `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~TokenSqlParserTests|FullyQualifiedName~StringSqlParserTests|FullyQualifiedName~UriSqlParserTests"` → failed as expected before implementation.
- `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~TokenSqlParserTests|FullyQualifiedName~StringSqlParserTests|FullyQualifiedName~UriSqlParserTests"` → passed (27 tests).

## Commit SHA
- `cdd29ba4d`

## Self-review
- Ran `pr-review-toolkit:code-reviewer` on the Task 2 diff: no high-confidence issues found.
- Ran `pr-review-toolkit:code-simplifier` and applied safe cleanup suggestions.

## Concerns
- URI `:above` / `:below` wording in the brief was ambiguous; implementation follows the tested parameterized behavior.

## Fix round 1

## Status
Completed.

## Files changed
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/StringSqlParser.cs`
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/StringSqlParserTests.cs`

## Implementation details
- Restored Text/TextOverflow selection to use escaped SQL-literal length for string search values.
- Added a regression test covering a 256-character raw value with an apostrophe that overflows only after escaping.

## Test evidence
- `dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~StringSqlParserTests"` → passed (9 tests).

## Commit
- `git commit -m "Fix escaped-length overflow selection" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"`

## Self-review
- Confirmed the parser now selects `TextOverflow` when escaping pushes the SQL literal past 256 characters, while parameters still carry the raw value plus wildcard.
