# Search Parameter SQL Parser Interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the SQL search parser substitutable in `SqlServerSearchService` tests through a narrow service-facing interface.

**Architecture:** Add `ISearchParameterSqlParser` with only the existing `ParseMultiple` contract and have `SearchParameterSqlParser` implement it. Change service-facing dependencies and test substitutes to the interface while retaining the concrete parser for internal chain parser composition and concrete parser tests.

**Tech Stack:** C# 13, .NET 9/10 SDK, xUnit, NSubstitute, EnsureThat, Microsoft.Health.Extensions.DependencyInjection

## Global Constraints

- `ISearchParameterSqlParser` exposes only `ParseMultiple`; `GetParser` remains a concrete implementation detail.
- Generated SQL, SQL parameters, search behavior, exceptions, cancellation, and logging must remain unchanged.
- Keep the existing singleton `.AsSelf().AsImplementedInterfaces()` registration unchanged.
- Keep concrete `SearchParameterSqlParser` construction in parser-focused tests, tools, and internal chain parser composition.
- Preserve the user's unrelated uncommitted changes in `Directory.Packages.props` and `global.json`; stage only files named by each task.

---

### Task 1: Introduce the service-facing parser contract

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/ISearchParameterSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs:28`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs:59,77`
- Modify: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlServerSearchServiceTests.cs:91,126,167,208`
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerFhirStorageTestsFixture.cs:298`
- Verify unchanged: `src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs:125-128`

**Interfaces:**
- Consumes: Existing `SearchParameterSqlParser.ParseMultiple(IDictionary<string, IList<string>>, SqlSearchOptions, HashingSqlQueryParameterManager, bool, ContinuationToken?, IncludesContinuationToken?)`.
- Produces: `ISearchParameterSqlParser.ParseMultiple(IDictionary<string, IList<string>>, SqlSearchOptions, HashingSqlQueryParameterManager, bool, ContinuationToken?, IncludesContinuationToken?)`, returning `string?`.

- [ ] **Step 1: Run the focused service tests to verify the existing proxy-construction failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlServerSearchServiceTests" --logger "console;verbosity=minimal"
```

Expected: FAIL during `SqlServerSearchServiceTests` construction because NSubstitute cannot create a `SearchParameterSqlParser` proxy without constructor arguments.

- [ ] **Step 2: Add the narrow parser interface**

Create `ISearchParameterSqlParser.cs` with the repository copyright header and this contract:

```csharp
#nullable enable

using System.Collections.Generic;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parses FHIR search parameters into a SQL search query.
    /// </summary>
    public interface ISearchParameterSqlParser
    {
        /// <summary>
        /// Parses multiple FHIR search parameters into a SQL search query.
        /// </summary>
        /// <param name="parameters">The search parameter values keyed by parameter name.</param>
        /// <param name="sqlSearchOptions">The SQL search options for the request.</param>
        /// <param name="parameterManager">The manager that adds values to the SQL command.</param>
        /// <param name="reuseQueryPlans">Whether generated SQL should support query plan reuse.</param>
        /// <param name="continuationToken">The optional search continuation token.</param>
        /// <param name="includesContinuationToken">The optional include continuation token.</param>
        /// <returns>The generated SQL query, or <see langword="null"/> when no query is generated.</returns>
        string? ParseMultiple(
            IDictionary<string, IList<string>> parameters,
            SqlSearchOptions sqlSearchOptions,
            HashingSqlQueryParameterManager parameterManager,
            bool reuseQueryPlans,
            ContinuationToken? continuationToken = null,
            IncludesContinuationToken? includesContinuationToken = null);
    }
}
```

- [ ] **Step 3: Implement the interface without changing parser behavior**

Change only the concrete class declaration in `SearchParameterSqlParser.cs`:

```csharp
public class SearchParameterSqlParser : ISearchParameterSqlParser
```

Do not change `ParseMultiple`, `GetParser`, constructor wiring, `ChainedSqlParser`, or `ReversedChainSqlParser`.

- [ ] **Step 4: Invert the service dependency**

Change the field and constructor parameter in `SqlServerSearchService.cs`:

```csharp
private readonly ISearchParameterSqlParser _searchParameterSqlParser;
```

```csharp
ISearchParameterSqlParser searchParameterSqlParser,
```

Keep the existing `EnsureArg.IsNotNull` validation and assignment unchanged.

- [ ] **Step 5: Replace concrete service-test substitutes**

At all four substitute sites in `SqlServerSearchServiceTests.cs` and the one site in `SqlServerFhirStorageTestsFixture.cs`, replace:

```csharp
Substitute.For<SearchParameterSqlParser>()
```

with:

```csharp
Substitute.For<ISearchParameterSqlParser>()
```

Do not replace concrete parser construction in `SearchParameterSqlParserTests`.

- [ ] **Step 6: Run the focused service tests**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlServerSearchServiceTests" --logger "console;verbosity=minimal"
```

Expected: PASS; the fixture and constructor-validation tests execute instead of failing during NSubstitute proxy construction.

- [ ] **Step 7: Run the concrete parser tests**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SearchParameterSqlParserTests" --logger "console;verbosity=minimal"
```

Expected: PASS with the existing parser test count, confirming the interface declaration did not alter SQL generation.

- [ ] **Step 8: Confirm registration still exposes both dependency surfaces**

Inspect `FhirServerBuilderSqlServerRegistrationExtensions.cs` and retain:

```csharp
services.Add<SearchParameterSqlParser>()
    .Singleton()
    .AsSelf()
    .AsImplementedInterfaces();
```

`AsImplementedInterfaces()` registers `ISearchParameterSqlParser` for `SqlServerSearchService`; `AsSelf()` preserves concrete resolution for parser internals. Do not add a duplicate registration.

- [ ] **Step 9: Build the SQL Server production project**

Run:

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --verbosity minimal
```

Expected: PASS with zero compile errors.

- [ ] **Step 10: Build an integration project containing the shared fixture**

Run:

```powershell
dotnet build test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --no-restore --verbosity minimal
```

Expected: PASS, confirming the shared fixture compiles against the interface and the service composition surface is valid.

- [ ] **Step 11: Run the complete SQL Server unit project**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --logger "console;verbosity=minimal"
```

Expected: `SqlServerSearchServiceTests` no longer contribute fixture-construction failures. Compare any remaining failures with the previously recorded baseline of 19 unrelated failures rather than changing unrelated behavior.

- [ ] **Step 12: Check the final diff and process state**

Run:

```powershell
git --no-pager diff --check
git --no-pager status --short
Get-Process -Name testhost,Microsoft.Health.Fhir.R4.Web -ErrorAction SilentlyContinue | Select-Object Id, ProcessName
```

Expected: no whitespace errors; only files named by this task plus the pre-existing `Directory.Packages.props` and `global.json` edits are modified; no testhost or FHIR server process started by this work remains running.

- [ ] **Step 13: Commit the parser interface change**

```powershell
git add -- src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\ISearchParameterSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\SearchParameterSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlServerSearchServiceTests.cs test\Microsoft.Health.Fhir.Shared.Tests.Integration\Persistence\SqlServerFhirStorageTestsFixture.cs
git commit -m "Add SQL search parser interface" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```
