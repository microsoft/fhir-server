# SQL Search Query Parameterization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore typed SQL parameters and the old selective query-plan reuse hash policy in the new SQL search parser so request values do not appear directly in command text.

**Architecture:** `SqlServerSearchService` creates the existing `HashingSqlQueryParameterManager` over each search command and passes it through `SearchParameterSqlParser` in request-scoped `ParserOptions`. Parser implementations bind runtime values against `VLatest` column metadata and emit returned names such as `@p0`; a focused hash appender restores the old `/* HASH ... */` behavior when plan reuse is disabled.

**Tech Stack:** .NET 10, C#, Microsoft.Data.SqlClient, Microsoft.Health.SqlServer parameter managers, xUnit, NSubstitute

## Global Constraints

- Match the old generator's selective parameterization and hash behavior.
- Keep `ResourceTypeId`, `ReferenceResourceTypeId`, and `SearchParamId` structural values inline.
- Parameterize all searchable values, resource IDs, paging values, and count values with schema-appropriate SQL types.
- Exclude TOP/count limits and continuation values from the parameter hash.
- Include include-truncation and row-limit predicate values in the parameter hash.
- Never fall back to embedding a runtime value when parameter metadata is unavailable.
- Keep parameter state request-scoped; parser service instances must remain concurrency-safe.
- Preserve the existing 2,048-value validation and SQL search semantics.
- Stop any E2E server process started during validation.

---

## File Structure

### New files

- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SqlSearchParameterHashAppender.cs`: emits the legacy parameter hash comment without coupling `SqlQueryBuilder` to `IndentedStringBuilder`.
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/ParserOptionsTests.cs`: verifies request-scoped parameter binding and explicit failure without a manager.
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SqlSearchParameterHashAppenderTests.cs`: verifies reuse-enabled and reuse-disabled hash behavior.
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SearchParameterSqlParserTests.cs`: verifies full-query parameterization, stable SQL shape, count policy, and combined reverse-chain behavior.
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/CompartmentSqlParserTests.cs`: verifies normal and SMART compartment owner-ID parameters.
- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParserTests.cs`: verifies include/revinclude limits and continuation values.

### Modified production files

- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/ParserOptions.cs`: carries the parameter manager and reuse state and exposes checked parameter-add methods.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseSqlParser.cs`: passes parser context into WHERE-clause generation and combined-chain join extraction.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs`: accepts the manager/reuse decision, parameterizes count and reverse-chain paging values, and emits the hash.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ChainedSqlParser.cs`: propagates the request-scoped manager into nested forward-chain options.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ReversedChainSqlParser.cs`: propagates the manager into nested reverse-chain options and parameterizes continuation surrogate IDs.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/*.cs`: parameterizes all base search values.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/CompositeParsers/BaseCompositeSqlParser.cs`: forwards the shared request context to component parsers.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IdSqlParser.cs`: parameterizes `_id` values without hashing them.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/LastUpdatedSqlParser.cs`: parameterizes surrogate bounds as continuation-style values.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/SortSqlParser.cs`: parameterizes sort continuation values.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParser.cs`: parameterizes include TOP, truncation, and row-limit values.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/RevIncludeSqlParser.cs`: applies the same policy to reverse includes.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/CompartmentSqlParser.cs`: parameterizes the compartment owner ID.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/SmartCompartmentSqlParser.cs`: reuses one owner-ID parameter across membership predicates and marks it for SMART handling when required.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ReversedChainSqlParser.cs`: parameterizes continuation surrogate IDs.
- `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`: constructs the manager and propagates the effective reuse decision through normal and include-only searches.

### Modified test support

- `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/ParserTestHelper.cs`: creates real `SqlCommand`-backed parser options.
- Existing parser tests under `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/`: assert placeholders and command parameters instead of embedded literals.

---

### Task 1: Add the Request-Scoped Parameter Context

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/ParserOptionsTests.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/ParserTestHelper.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/ParserOptions.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/CompositeParsers/BaseCompositeSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ChainedSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ReversedChainSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`
- Modify: all seven overrides in `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/`
- Modify: existing base/composite parser tests to pass `ParserOptions`

**Interfaces:**
- Produces: `HashingSqlQueryParameterManager? ParserOptions.ParameterManager { get; init; }`
- Produces: `bool ParserOptions.ReuseQueryPlans { get; init; }`
- Produces: `object ParserOptions.AddParameter(Column column, object value, bool includeInHash)`
- Produces: `SqlParameter ParserOptions.AddParameter(object value, bool includeInHash)`
- Changes: `BaseSqlParser.BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")`
- Changes: `SearchParameterSqlParser.ParseMultiple(..., HashingSqlQueryParameterManager parameterManager, bool reuseQueryPlans, ContinuationToken? continuationToken = null, IncludesContinuationToken? includesContinuationToken = null)`
- Changes: `SearchIncludeImpl(SqlSearchOptions options, bool reuseQueryPlans, CancellationToken cancellationToken)`

- [ ] **Step 1: Write failing context tests**

```csharp
[Fact]
public void GivenColumnValue_WhenAddParameter_ThenAddsTypedCommandParameter()
{
    using var command = new SqlCommand();
    var options = ParserTestHelper.CreateParserOptions(command);

    object result = options.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: true);

    Assert.Equal("@p0", result.ToString());
    Assert.Equal("Smith%", command.Parameters["@p0"].Value);
    Assert.Equal(SqlDbType.NVarChar, command.Parameters["@p0"].SqlDbType);
}

[Fact]
public void GivenNoManager_WhenAddParameter_ThenThrows()
{
    var options = new ParserOptions();

    Assert.Throws<InvalidOperationException>(
        () => options.AddParameter(VLatest.StringSearchParam.Text, "Smith", includeInHash: true));
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ParserOptionsTests"
```

Expected: FAIL because `CreateParserOptions`, `ParameterManager`, and `AddParameter` do not exist.

- [ ] **Step 3: Implement the context and test helper**

Add checked delegation in `ParserOptions`:

```csharp
public HashingSqlQueryParameterManager? ParameterManager { get; init; }

public bool ReuseQueryPlans { get; init; }

public object AddParameter(Column column, object value, bool includeInHash)
{
    if (ParameterManager == null)
    {
        throw new InvalidOperationException("A SQL parameter manager is required to generate a search query.");
    }

    return ParameterManager.AddParameter(column, value, includeInHash);
}

public SqlParameter AddParameter(object value, bool includeInHash)
{
    if (ParameterManager == null)
    {
        throw new InvalidOperationException("A SQL parameter manager is required to generate a search query.");
    }

    return ParameterManager.AddParameter(value, includeInHash);
}
```

Add this helper:

```csharp
public static ParserOptions CreateParserOptions(SqlCommand command, bool reuseQueryPlans = true)
{
    var manager = new HashingSqlQueryParameterManager(
        new SqlQueryParameterManager(command.Parameters));

    return new ParserOptions
    {
        ParameterManager = manager,
        ReuseQueryPlans = reuseQueryPlans,
    };
}
```

Change `BuildWhereClause` and all overrides/callers to accept `ParserOptions options`. At this checkpoint, keep existing literal clause generation unchanged; this is a mechanical context-plumbing change.

Copy the request state into every nested chain option:

```csharp
ParameterManager = options.ParameterManager,
ReuseQueryPlans = options.ReuseQueryPlans,
```

Set the same properties on the root `ParserOptions` created by `ParseMultiple`.

At both command creation sites, construct the manager over `sqlCommand.Parameters` and pass the effective reuse decision:

```csharp
bool canReuseQueryPlan = reuseQueryPlans &&
    _queryPlanReuseChecker.CanReuseQueryPlan(clonedSearchOptions);

var parameterManager = new HashingSqlQueryParameterManager(
    new SqlQueryParameterManager(sqlCommand.Parameters));
```

Propagate the original `reuseQueryPlans` flag through direct and nested `SearchIncludeImpl` calls, and apply the checker inside the include path.

- [ ] **Step 4: Run parser tests**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser"
```

Expected: PASS with existing SQL expectations plus the new context tests.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser
git commit -m "Add SQL parser parameter context"
```

### Task 2: Parameterize Token, String, and URI Values

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/TokenSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/StringSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/UriSqlParser.cs`
- Modify: corresponding tests under `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/`

**Interfaces:**
- Consumes: `ParserOptions.AddParameter(Column, object, bool)`
- Produces: clauses containing parameter names only; LIKE wildcards are stored in parameter values

- [ ] **Step 1: Replace literal expectations with failing parameter assertions**

Add representative tests:

```csharp
[Fact]
public void GivenStringPrefix_WhenBuildWhereClause_ThenParameterizesPattern()
{
    using var command = new SqlCommand();
    var options = ParserTestHelper.CreateParserOptions(command);

    string sql = _parser.BuildWhereClause("O'Brien", string.Empty, options);

    Assert.Equal("(t.Text like @p0)", sql);
    Assert.DoesNotContain("O'Brien", sql, StringComparison.Ordinal);
    Assert.Equal("O'Brien%", command.Parameters["@p0"].Value);
}
```

Add equivalent tests for token code/system/text and URI exact/above/below. Parameter names must never be surrounded by SQL string quotes.

- [ ] **Step 2: Run the three test classes and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~TokenSqlParserTests|FullyQualifiedName~StringSqlParserTests|FullyQualifiedName~UriSqlParserTests"
```

Expected: FAIL because raw values still appear in clauses and no command parameters are added.

- [ ] **Step 3: Bind values using schema metadata**

Use:

```csharp
options.AddParameter(VLatest.TokenSearchParam.Code, code, includeInHash: true);
options.AddParameter(VLatest.TokenSearchParam.CodeOverflow, overflow, includeInHash: true);
options.AddParameter(VLatest.TokenText.Text, $"{value}%", includeInHash: true);
options.AddParameter(VLatest.System.Value, system, includeInHash: true);
options.AddParameter(VLatest.StringSearchParam.Text, $"{value}%", includeInHash: true);
options.AddParameter(VLatest.StringSearchParam.TextOverflow, value, includeInHash: true);
options.AddParameter(VLatest.UriSearchParam.Uri, value, includeInHash: true);
```

For `:contains`, bind `%value%`; for URI `:above`, bind `value%` on the left-hand side; for URI `:below`, bind `value%` on the right-hand side. Remove obsolete SQL quote escaping because SqlClient now owns serialization.

- [ ] **Step 4: Run the focused tests**

Expected: PASS and no tested request value appears in SQL.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\TokenSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\StringSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\UriSqlParser.cs src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser\BaseParsers
git commit -m "Parameterize string-like SQL searches"
```

### Task 3: Parameterize Date, Number, and Quantity Values

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/DateTimeSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/NumberSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/QuantitySqlParser.cs`
- Modify: corresponding tests under `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/`

**Interfaces:**
- Consumes: request-scoped `ParserOptions`
- Produces: DateTimeOffset/decimal parameters typed from `VLatest` metadata

- [ ] **Step 1: Write failing typed-value tests**

```csharp
[Fact]
public void GivenNumberRange_WhenBuildWhereClause_ThenUsesDecimalParameters()
{
    using var command = new SqlCommand();
    var options = ParserTestHelper.CreateParserOptions(command);

    string sql = _parser.BuildWhereClause("gt3.14", string.Empty, options);

    Assert.Equal("t.HighValue > @p0", sql);
    Assert.Equal(3.14m, command.Parameters["@p0"].Value);
    Assert.Equal(SqlDbType.Decimal, command.Parameters["@p0"].SqlDbType);
}
```

Add date equality tests that expect two typed boundary parameters and quantity tests that expect numeric, system, and code parameters.

- [ ] **Step 2: Run the three test classes and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DateTimeSqlParserTests|FullyQualifiedName~NumberSqlParserTests|FullyQualifiedName~QuantitySqlParserTests"
```

- [ ] **Step 3: Implement typed numeric and temporal binding**

Parse numeric input with invariant culture into `decimal`, then bind against `VLatest.NumberSearchParam.LowValue` or `HighValue`. Bind date boundaries against `VLatest.DateTimeSearchParam.StartDateTime` and `EndDateTime`. Bind quantity systems and codes with `VLatest.System.Value` and `VLatest.QuantityCode.Value`.

For equality and not-equality ranges, allocate the same number of parameters for every query of that shape. For `ap`, bind numeric values and keep only arithmetic operators/constants in SQL.

- [ ] **Step 4: Run the focused tests**

Expected: PASS, including non-English current-culture coverage for decimal input.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\DateTimeSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\NumberSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\QuantitySqlParser.cs src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser\BaseParsers
git commit -m "Parameterize numeric and date SQL searches"
```

### Task 4: Parameterize Reference and Composite Searches

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseParsers/ReferenceSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/CompositeParsers/BaseCompositeSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/BaseSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/BaseParsers/ReferenceSqlParserTests.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/CompositeParsers/CompositeParserTests.cs`

**Interfaces:**
- Consumes: context-aware `BuildWhereClause`
- Produces: parameterized `GetSearchJoinInfo(..., ParserOptions options)` for combined reverse-chain SQL

- [ ] **Step 1: Write failing reference and composite assertions**

Verify relative reference IDs and absolute base URIs become parameters, while resolved reference type IDs remain numeric literals. Verify each composite component adds parameters in component order and generated SQL contains no component input.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ReferenceSqlParserTests|FullyQualifiedName~CompositeParserTests"
```

- [ ] **Step 3: Implement reference and context forwarding**

Bind:

```csharp
options.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceId, resourceId, includeInHash: true);
options.AddParameter(VLatest.ReferenceSearchParam.BaseUri, baseUri, includeInHash: true);
options.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, resourceTypeId, includeInHash: true);
```

The manager intentionally returns the resource type ID as a literal. Forward the same `ParserOptions` to every composite component. Add `ParserOptions options` to `GetSearchJoinInfo` and use it when the combined reverse-chain path builds leaf predicates.

- [ ] **Step 4: Run all parser tests**

Expected: PASS; combined reverse-chain clause generation uses the request manager.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\BaseParsers\ReferenceSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\CompositeParsers src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser
git commit -m "Parameterize reference and composite searches"
```

### Task 5: Parameterize IDs, Paging, Sort, and Include Limits

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParserTests.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/ParserUtil.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IdSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/LastUpdatedSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/SortSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/IncludeSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/RevIncludeSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/ReversedChainSqlParser.cs`
- Modify: corresponding existing tests

**Interfaces:**
- Produces: `_id` parameters bound with `VLatest.Resource.ResourceId`, `includeInHash: false`
- Produces: TOP/continuation parameters with `includeInHash: false`
- Produces: include predicate limits with `includeInHash: true`
- Changes: `SortSqlParser.CreateSortCte(..., ParserOptions options, ...)`

- [ ] **Step 1: Write failing policy tests**

Cover:

```csharp
Assert.Equal("123", command.Parameters["@p0"].Value);
Assert.DoesNotContain("123", sql, StringComparison.Ordinal);
Assert.Contains("TOP (@p0)", sql, StringComparison.Ordinal);
Assert.Contains("count_big(*) over() > @p1", sql, StringComparison.Ordinal);
```

Verify sort continuation uses separate parameters for equality and range comparisons, all excluded from hashing.

- [ ] **Step 2: Run focused special-parser tests and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdSqlParserTests|FullyQualifiedName~LastUpdatedSqlParserTests|FullyQualifiedName~SortSqlParserTests|FullyQualifiedName~IncludeSqlParserTests|FullyQualifiedName~ParserUtilTests"
```

- [ ] **Step 3: Implement paging and limit binding**

Use column metadata for IDs and continuation surrogate IDs. Use untyped `AddParameter(value, includeInHash)` for TOP values. Preserve these old distinctions:

```csharp
string top = options.AddParameter(options.Count + 1, includeInHash: false).ToString();
string includeTop = options.AddParameter(options.IncludeCount + 1, includeInHash: false).ToString();
string partialThreshold = options.AddParameter(options.IncludeCount, includeInHash: true).ToString();
string rowLimit = options.AddParameter(options.Count, includeInHash: true).ToString();
```

Use parameter names without surrounding quotes. Keep resource type IDs literal through the typed manager overload.

- [ ] **Step 4: Run all SQL parser tests**

Expected: PASS with stable paging SQL shapes.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser
git commit -m "Parameterize SQL search paging and includes"
```

### Task 6: Parameterize Compartment and SMART Scope Values

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SpecialParsers/CompartmentSqlParserTests.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/CompartmentSqlParser.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SpecialParsers/SmartCompartmentSqlParser.cs`

**Interfaces:**
- Consumes: `ParserOptions.AddParameter`
- Produces: one owner-ID parameter reused by SMART membership branches

- [ ] **Step 1: Write failing compartment tests**

Build real command-backed options and mocked compartment/model definitions. Assert:

```csharp
Assert.Single(command.Parameters.Cast<SqlParameter>().Where(p => Equals(p.Value, "patient-id")));
Assert.DoesNotContain("patient-id", sql, StringComparison.Ordinal);
Assert.Contains("ref1.ReferenceResourceId = @p0", sql, StringComparison.Ordinal);
Assert.Contains("r.ResourceId = @p0", smartSql, StringComparison.Ordinal);
```

Also assert compartment resource type and search parameter IDs remain literals.

- [ ] **Step 2: Run compartment tests and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~CompartmentSqlParserTests"
```

- [ ] **Step 3: Implement owner-ID binding and reuse**

Create the owner parameter once with:

```csharp
object ownerId = options.AddParameter(
    VLatest.Resource.ResourceId,
    value,
    includeInHash: true);
```

The existing manager excludes `Resource.ResourceId` from the value hash. Reuse `ownerId` in both SMART reference-membership and owner-resource predicates. Continue emitting resource/search parameter IDs as literals.

- [ ] **Step 4: Run compartment and parser tests**

Expected: PASS with one owner-ID parameter per SMART membership query.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\SpecialParsers\CompartmentSqlParser.cs src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser\SpecialParsers\SmartCompartmentSqlParser.cs src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\SqlSearchParser\SpecialParsers\CompartmentSqlParserTests.cs
git commit -m "Parameterize compartment search values"
```

### Task 7: Restore Parameter Hashing and Verify Full Query Shapes

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SqlSearchParameterHashAppender.cs`
- Create: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SqlSearchParameterHashAppenderTests.cs`
- Create: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlSearchParser/SearchParameterSqlParserTests.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchParser/SearchParameterSqlParser.cs`

**Interfaces:**
- Consumes: the parameterized `ParseMultiple` signature from Task 1
- Produces: `SqlSearchParameterHashAppender.Append(SqlQueryBuilder builder, HashingSqlQueryParameterManager manager, bool reuseQueryPlans)`

- [ ] **Step 1: Write failing hash and full-query tests**

Hash tests:

```csharp
[Fact]
public void GivenHashableValuesAndReuseDisabled_WhenAppend_ThenAddsLegacyHashComment()
{
    using var command = new SqlCommand();
    var manager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
    manager.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: true);
    var builder = new SqlQueryBuilder().AppendLine("SELECT 1");

    SqlSearchParameterHashAppender.Append(builder, manager, reuseQueryPlans: false);

    Assert.Contains(SqlSearchConstants.ParametersHashStart, builder.ToString(), StringComparison.Ordinal);
    Assert.Contains("params=@p0", builder.ToString(), StringComparison.Ordinal);
}
```

Add tests proving:

- Reuse enabled adds no comment.
- Two same-shape searches with different values produce identical SQL when reuse is enabled.
- The same searches produce different hash comments when reuse is disabled.
- The query hash calculator returns the same custom-query hash for both disabled-reuse SQL strings.
- Combined reverse-chain SQL contains placeholders and retains one shared reference CTE.

- [ ] **Step 2: Run new tests and verify failure**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParameterHashAppenderTests|FullyQualifiedName~SearchParameterSqlParserTests"
```

- [ ] **Step 3: Implement the hash appender**

Use a temporary `IndentedStringBuilder` to call the existing hash manager:

```csharp
var hash = new IndentedStringBuilder(new StringBuilder());
manager.AppendHash(hash);
manager.AppendHashedParameterNames(hash);

builder.Append(SqlSearchConstants.ParametersHashStart)
    .Append(hash.ToString())
    .AppendLine(SqlSearchConstants.ParametersHashEnd);
```

Return without appending when reuse is enabled or the selected parameter set is empty.

- [ ] **Step 4: Append the hash from the orchestrator**

Immediately before returning generated SQL, call:

```csharp
SqlSearchParameterHashAppender.Append(
    sqlBuilder,
    parserOptions.ParameterManager ??
        throw new InvalidOperationException("A SQL parameter manager is required to generate a search query."),
    parserOptions.ReuseQueryPlans);
```

The new parser emits one combined command, so one full parameter hash covers its filtering and SMART include CTEs. Verify custom-query hash calculation still strips this comment and custom stored procedures retain the populated command parameters.

- [ ] **Step 5: Run parser and search-service tests**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --filter "FullyQualifiedName~SqlSearchParser|FullyQualifiedName~SqlServerSearchServiceTests|FullyQualifiedName~SqlQueryHashCalculatorTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search
git commit -m "Restore SQL search query parameterization"
```

### Task 8: Validate Security, Query Shape, and Search Behavior

**Files:**
- Modify only files implicated by validation failures.

**Interfaces:**
- Consumes: completed parameterized parser pipeline
- Produces: verified build and search behavior with no residual embedded request values

- [ ] **Step 1: Scan for remaining literal request-value construction**

Run:

```powershell
rg -n "EscapeSqlValue|Replace\\(\"'\"|LIKE N'\\{|ReferenceResourceId = '|ResourceId = '|continuationPoint.*'" src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlSearchParser
```

Expected: no runtime value serialization remains. Structural numeric ID interpolation is allowed.

- [ ] **Step 2: Run the SQL Server project build**

Run:

```powershell
dotnet build src\Microsoft.Health.Fhir.SqlServer\Microsoft.Health.Fhir.SqlServer.csproj --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Run the complete SQL Server unit-test project**

Run:

```powershell
dotnet test src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 4: Exercise representative search E2E coverage**

Run:

```powershell
dotnet test test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --no-restore --filter "FullyQualifiedName~Microsoft.Health.Fhir.Tests.E2E.Rest.Search|FullyQualifiedName~Microsoft.Health.Fhir.Tests.E2E.Rest.CompartmentTests|FullyQualifiedName~MemberMatch"
```

Expected: behavior matches the pre-parameterization parser and no test hangs.

- [ ] **Step 5: Stop test infrastructure and verify no locking process remains**

Stop only the exact server/testhost processes started by this task using their recorded process IDs, then verify those PIDs no longer exist.

- [ ] **Step 6: Review the final diff**

Run:

```powershell
git --no-pager diff --check
git status --short
git --no-pager diff HEAD~7 --stat
```

Expected: no whitespace errors, no temporary files, and changes limited to parser parameterization, service integration, tests, and approved docs.

- [ ] **Step 7: Commit validation fixes**

```powershell
git add -u src\Microsoft.Health.Fhir.SqlServer src\Microsoft.Health.Fhir.SqlServer.UnitTests test\Microsoft.Health.Fhir.Shared.Tests.E2E
git commit -m "Fix SQL parameterization validation issues"
```

Skip this commit when validation required no changes.
