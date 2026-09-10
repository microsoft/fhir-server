# SQL Server Search Service File Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Divide `SqlServerSearchService` into cohesive partial-class files without changing search behavior, SQL generation, dependency injection, or test-visible APIs.

**Architecture:** Keep one runtime `internal partial class SqlServerSearchService`. Move existing members into responsibility-based files for request orchestration, execution, include paging, Query Store diagnostics, surrogate-range operations, and statistics; do not introduce collaborators or registrations.

**Tech Stack:** C# 14, .NET 10, Microsoft.Data.SqlClient, xUnit/YTest Microsoft Testing Platform, PowerShell, Git.

## Global Constraints

- Preserve the `SqlServerSearchService` constructor signature and dependency-injection registration.
- Preserve method signatures, member visibility, field types, initialization order, exception behavior, cancellation behavior, logging, SQL text, SQL parameters, and continuation-token contents.
- Do not change query-plan reuse, custom-query selection, include, reverse-include, SMART, sort, paging, range, or Query Store behavior.
- Keep the dormant statistics creation implementation commented and move it intact into `SqlServerSearchService.Statistics.cs`.
- Delete only the approved retired expression-tree generation and token/ID optimization comment blocks.
- Do not alter unrelated working-tree changes in `Directory.Packages.props`, `global.json`, or `SqlServerSearchService.cs`; inspect and preserve them before editing.
- Run build and tests sequentially to avoid file locks, and stop only test/server processes started by this work.
- Every commit must include `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.

---

## Target File Structure

| File | Responsibility |
|---|---|
| `SqlServerSearchService.cs` | Constructor, shared dependencies, `SearchAsync`, two-phase request orchestration, total-count orchestration, `IsValidResourceType` |
| `SqlServerSearchService.Execution.cs` | `RunSearch`, `SearchImpl`, normal command execution, result materialization, `ReadWrapper`, debug SQL logging |
| `SqlServerSearchService.Include.cs` | `SearchIncludeImpl` and include continuation execution |
| `SqlServerSearchService.QueryStore.cs` | Long-running-query flags, normalization, Query Store lookup, concurrency gate, circuit breaker |
| `SqlServerSearchService.SurrogateRanges.cs` | Range search, range enumeration, used resource types, range command helpers |
| `SqlServerSearchService.Statistics.cs` | Statistics flag, active read/test hooks, nested stats cache, preserved dormant stats creation |

## Task 1: Capture the Baseline and Remove Retired Disabled Code

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`
- Inspect: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlServerSearchServiceTests.cs`

**Interfaces:**
- Consumes: current monolithic `internal class SqlServerSearchService`
- Produces: behaviorally identical monolith without unrelated retired comment blocks

- [ ] **Step 1: Inspect and record pre-existing worktree changes**

Run:

```powershell
git status --short
git --no-pager diff -- src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs
New-Item -ItemType Directory -Force .superpowers\sdd\2026-09-10-sql-server-search-service-file-split | Out-Null
git rev-parse HEAD | Set-Content .superpowers\sdd\2026-09-10-sql-server-search-service-file-split\base-sha.txt
```

Expected: identify user changes that must survive the refactor. Do not reset or overwrite them.

- [ ] **Step 2: Run and record the focused baseline**

Run:

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests|FullyQualifiedName~SqlServerSearchServiceQueryStoreTests|FullyQualifiedName~SqlServerSearchServiceOptimizationTests|FullyQualifiedName~SqlServerSearchServiceCircuitBreakerTests"
```

Expected: build succeeds. Record exact test totals and failures before editing; later tasks must not add failures.

- [ ] **Step 3: Remove only the approved retired blocks**

Delete these disabled blocks from `SqlServerSearchService.cs`:

```text
SearchImpl's commented TryExtractGetResourcesByTokensParams branch
CreateDefaultSearchExpression and AttachSmartCompartmentMembership
ApplyDateEqualitySemantics
PopulateGetResourcesByTokensCommand
TryExtractGetResourcesByTokensParams
TryExtractTokenWithSystem
GetResourcesByIdsAsync
TryExtractResourceKeys
Token
TokenListRowGenerator
```

Preserve these blocks unchanged:

```text
CreateStats
ResourceSearchParamStats.Create
ProcessUnionBranch
ProcessNotExistsForStats
CollectNotExistsLeaves
ProcessPredicateForStats
HandleSearchParameterExpression
CollectReferenceResourceTypes
CollectResourceTypesFromExpression
```

- [ ] **Step 4: Remove imports used only by deleted blocks**

Run:

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
```

Remove only compiler-reported unnecessary imports introduced by this cleanup. Expected: zero errors.

- [ ] **Step 5: Re-run the focused baseline**

Run:

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests|FullyQualifiedName~SqlServerSearchServiceQueryStoreTests|FullyQualifiedName~SqlServerSearchServiceOptimizationTests|FullyQualifiedName~SqlServerSearchServiceCircuitBreakerTests"
```

Expected: exact same pass/fail set as Step 2.

- [ ] **Step 6: Commit the cleanup**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs
git commit -m "Remove retired SQL search service code" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Extract Query Store Diagnostics

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.QueryStore.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlServerSearchServiceQueryStoreTests.cs`
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlServerSearchServiceCircuitBreakerTests.cs`

**Interfaces:**
- Consumes: private `_sqlRetryService`, `_logger`, and shared initialization from the partial class
- Produces: unchanged internal static Query Store test hooks on `SqlServerSearchService`

- [ ] **Step 1: Make the service partial**

Change:

```csharp
internal class SqlServerSearchService : SearchService
```

to:

```csharp
internal partial class SqlServerSearchService : SearchService
```

- [ ] **Step 2: Create the Query Store partial shell**

Create:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal partial class SqlServerSearchService
    {
    }
}
```

Add only the using directives required by members moved in the next steps.

- [ ] **Step 3: Move Query Store-owned state**

Move these declarations without changing values or modifiers:

```text
LongRunningQueryDetailsParameterId
LongRunningQueryDetailsThresholdId
LongRunningThresholdMillisecondsDefault
NewLineSeparators
WhitespacePattern
QueryStoreLookupTimeoutSeconds
MaxConcurrentQueryStoreLookups
_queryStoreLookupGate
QueryStoreCircuitBreakerFailureThreshold
QueryStoreCircuitBreakerCooldown
_queryStoreConsecutiveFailures
_queryStoreCircuitOpenUntilTicks
_longRunningQueryDetails
_longRunningThreshold
```

Keep `InitializeProcessingFlags` in the primary file because it initializes both diagnostics and statistics flags.

- [ ] **Step 4: Move Query Store methods**

Move these methods without body changes:

```text
StripQueryPreambleLines
SplitIntoSearchFragments
StripAllWhitespace
StripDboSchemaPrefix
ExtractParameterHash
FireAndForgetQueryStoreLookup
TryEnterQueryStoreCircuit
RecordQueryStoreSuccess
RecordQueryStoreFailure
SetQueryStoreCircuitStateForTests
GetQueryStoreCircuitOpenUntilTicksForTests
LogQueryStoreByTextAsync
AppendQueryStoreResults
```

- [ ] **Step 5: Build and run Query Store tests**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceQueryStoreTests|FullyQualifiedName~SqlServerSearchServiceCircuitBreakerTests"
```

Expected: build succeeds and the focused tests match the Task 1 baseline.

- [ ] **Step 6: Review the move**

```powershell
git --no-pager diff --color-moved=zebra -- src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.QueryStore.cs
git diff --check
```

Expected: method bodies are recognized as moved; no behavior changes or whitespace errors.

- [ ] **Step 7: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.QueryStore.cs
git commit -m "Extract SQL search Query Store diagnostics" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Extract Surrogate Range Operations

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.SurrogateRanges.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`

**Interfaces:**
- Consumes: `_model`, `_sqlRetryService`, `_logger`, `_sqlServerDataStoreConfiguration`, `_compressedRawResourceConverter`, `_requestContextAccessor`
- Produces: unchanged `SearchBySurrogateIdRange`, `GetSurrogateIdRanges`, and `GetUsedResourceTypes` APIs

- [ ] **Step 1: Create the range partial shell**

Create:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal partial class SqlServerSearchService
    {
    }
}
```

Add only imports required by the range members.

- [ ] **Step 2: Move range command helpers**

Move:

```text
ContainsStartSurrogateId
PopulateSqlCommandFromQueryHints(SqlSearchOptions, SqlCommand)
PopulateSqlCommandFromQueryHints(SqlCommand, short, long, long, long?, bool?, bool?)
PopulateGetResourceSurrogateIdRangesCommand
GetSurrogateIdRangeCommandTimeout
ReaderToSurrogateIdRange
ReaderGetUsedResourceTypes
```

`SearchImpl` may continue calling these private members across the partial class.

- [ ] **Step 3: Move range APIs**

Move:

```text
SearchBySurrogateIdRange(string, long, long, CancellationToken)
SearchBySurrogateIdRange(string, long, long, long?, long?, CancellationToken, bool, bool)
GetSurrogateIdRanges
GetUsedResourceTypes
```

Preserve XML documentation with the methods.

- [ ] **Step 4: Build and run focused service tests**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests"
```

Expected: build succeeds; focused results match baseline.

- [ ] **Step 5: Review and commit**

Inspect the two files with `git diff --color-moved=zebra`, then:

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.SurrogateRanges.cs
git commit -m "Extract SQL search surrogate range operations" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Extract Statistics Support and Preserve Dormant Logic

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.Statistics.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlServerSearchServiceTests.cs`
- Integration: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Features/Search/SqlServerSearchServiceIntegrationTests.cs`
- Integration: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerCreateStatsTests.cs`
- Integration: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerCreateStatsForSmartTests.cs`

**Interfaces:**
- Consumes: `_sqlRetryService`, `_logger`, `_locker`
- Produces: unchanged `GetStatsFromCache`, `ResetReferenceResourceTypeFilteredStatsCache`, `GetStatsFromDatabase`, `GetKeyColumns`, and `ResourceSearchParamStats`

- [ ] **Step 1: Create the statistics partial shell**

Create:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal partial class SqlServerSearchService
    {
    }
}
```

Add only imports required by statistics members.

- [ ] **Step 2: Move active statistics state and hooks**

Move:

```text
ReferenceResourceTypeFilteredStatsParameterId
_referenceResourceTypeFilteredStats
GetStatsFromCache
ResetReferenceResourceTypeFilteredStatsCache
GetStatsFromDatabase(CancellationToken)
GetStatsFromDatabase(ISqlRetryService, ILogger<SqlServerSearchService>, CancellationToken)
GetKeyColumns
```

Keep the call that initializes `_referenceResourceTypeFilteredStats` in `InitializeProcessingFlags`.

- [ ] **Step 3: Move dormant statistics logic intact**

Move the complete commented `CreateStats` method and the complete `ResourceSearchParamStats` nested class into the statistics partial. Do not:

```text
uncomment code
reformat method bodies
rename members
change feature-flag behavior
delete commented expression walkers
split the block across files
```

- [ ] **Step 4: Run statistics-focused unit tests**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests"
```

Expected: `GetKeyColumns` and construction tests match baseline.

- [ ] **Step 5: Compile integration tests**

```powershell
dotnet build test\Microsoft.Health.Fhir.R4.Tests.Integration\Microsoft.Health.Fhir.R4.Tests.Integration.csproj --no-restore --nologo
```

Expected: all internal statistics hooks remain resolvable. Do not run database integration tests unless their infrastructure is configured.

- [ ] **Step 6: Review and commit**

Verify the dormant block is byte-for-byte unchanged apart from indentation required by its new file. Then:

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.Statistics.cs
git commit -m "Extract SQL search statistics support" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Extract Include Continuation Execution

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.Include.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`

**Interfaces:**
- Consumes: `_sqlRetryService`, `_logger`, `_model`, `_requestContextAccessor`, `_compressedRawResourceConverter`, `_queryHashCalculator`, `_queryPlanReuseChecker`, `_searchParameterSqlParser`, `ReadWrapper`, `LogSqlCommand`, `FireAndForgetQueryStoreLookup`
- Produces: unchanged private `Task<SearchResult> SearchIncludeImpl(SqlSearchOptions, bool, CancellationToken)`

- [ ] **Step 1: Create the include partial shell**

Create:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal partial class SqlServerSearchService
    {
    }
}
```

Add only imports needed for SQL execution, resource materialization, and continuation tokens.

- [ ] **Step 2: Move `SearchIncludeImpl`**

Move the complete method and its comments without body changes:

```csharp
private async Task<SearchResult> SearchIncludeImpl(
    SqlSearchOptions sqlSearchOptions,
    bool reuseQueryPlans,
    CancellationToken cancellationToken)
```

Do not move `SearchAsync`; it owns request-level multi-phase orchestration rather than execution of one include page.

- [ ] **Step 3: Build and run parser/service coverage**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests|FullyQualifiedName~SqlSearchParser"
```

Expected: build succeeds and results match baseline.

- [ ] **Step 4: Review and commit**

Inspect moved code with `git diff --color-moved=zebra`, then:

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.Include.cs
git commit -m "Extract SQL include search execution" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Extract Normal Search Execution

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.Execution.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`

**Interfaces:**
- Consumes: shared constructor dependencies, range helpers, include execution, Query Store diagnostics
- Produces: unchanged `RunSearch`, `SearchImpl`, `ReadWrapper`, and SQL debug logging

- [ ] **Step 1: Create the execution partial shell**

Create:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal partial class SqlServerSearchService
    {
    }
}
```

Add only imports required by normal execution and result materialization.

- [ ] **Step 2: Move normal execution**

Move without body changes:

```text
RunSearch
SearchImpl
ReadWrapper
EnableTimeAndIoMessageLogging
LogSqlCommand
```

Keep `SearchAsync` in the primary file. It controls request-level two-phase sort/include/count behavior; `RunSearch` and `SearchImpl` execute one search phase.

- [ ] **Step 3: Build**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
```

Expected: zero errors and no new warnings.

- [ ] **Step 4: Run search-service and parser tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchServiceTests|FullyQualifiedName~SqlServerSearchServiceQueryStoreTests|FullyQualifiedName~SqlServerSearchServiceOptimizationTests|FullyQualifiedName~SqlServerSearchServiceCircuitBreakerTests|FullyQualifiedName~SqlSearchParser"
```

Expected: exact baseline failures only.

- [ ] **Step 5: Review and commit**

Inspect moved code and verify `SearchImpl` still:

```text
creates the same command parameters
selects custom queries using the same hash
materializes match/include rows identically
constructs continuation tokens identically
invokes include continuation identically
logs SQL exceptions and long-running queries identically
```

Then:

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.Execution.cs
git commit -m "Extract SQL search execution" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: Normalize File Ownership and Validate the Complete Split

**Files:**
- Modify only if needed: all six `SqlServerSearchService*.cs` files
- Modify only if ownership is clearer: existing `SqlServerSearchService*Tests.cs` files

**Interfaces:**
- Consumes: all partial files from Tasks 2-6
- Produces: final cohesive file layout and verified behavior

- [ ] **Step 1: Check final ownership**

Confirm the primary file contains only:

```text
shared fields and constructor
StoredProcedureLayerIsEnabled
Model
InitializeProcessingFlags
SearchAsync
IsValidResourceType
```

Move any misplaced helper to its responsibility owner. Do not create a generic `Helpers` partial.

- [ ] **Step 2: Clean per-file imports**

Build once, remove only unused imports, and run:

```powershell
git diff --check
```

- [ ] **Step 3: Run the final SQL Server build**

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo
```

Expected: zero errors and no new warnings.

- [ ] **Step 4: Run all SQL Server search-service tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlServerSearchService|FullyQualifiedName~SqlSearchParser"
```

Expected: no failures beyond the recorded Task 1 baseline.

- [ ] **Step 5: Run the complete SQL Server unit project**

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore
```

Expected: no failures beyond the recorded baseline; compare test names, not only counts.

- [ ] **Step 6: Run representative E2E tests when configured**

```powershell
dotnet test test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --no-restore --filter "FullyQualifiedName~Microsoft.Health.Fhir.Tests.E2E.Rest.Search|FullyQualifiedName~Microsoft.Health.Fhir.Tests.E2E.Rest.CompartmentTests|FullyQualifiedName~MemberMatch"
```

Expected: pass when `testconfiguration.json` and backing stores are configured. If infrastructure is unavailable, report the exact fixture/setup failure rather than changing production code.

- [ ] **Step 7: Confirm no locking test process remains**

Record PIDs before tests. After tests, inspect:

```powershell
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -eq 'testhost.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -notmatch 'MSBuild.dll') } |
    Select-Object ProcessId, Name, CommandLine
```

Stop only PIDs started by this plan.

- [ ] **Step 8: Final review**

Review the complete range:

```powershell
$splitBase = Get-Content .superpowers\sdd\2026-09-10-sql-server-search-service-file-split\base-sha.txt
git --no-pager diff --stat "$splitBase..HEAD"
git --no-pager diff --color-moved=zebra "$splitBase..HEAD" -- src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService*.cs
```

Expected: approved dead-code deletion plus moved code; no functional edits.

- [ ] **Step 9: Commit final ownership cleanup if needed**

If Step 1 or 2 changed files:

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService*.cs
git commit -m "Finalize SQL search service file ownership" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

If no files changed, do not create an empty commit.
