# Scalar Temporal Birthdate Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the branch-local exact-day-only date rewrite with metadata-driven scalar temporal identification, birthdate-only activation, partial-date SQL rewrite support, and non-PHI observability.

**Architecture:** Add reusable derived metadata (`IsScalarTemporal`) in Core/Shared Core and propagate it through the same paths that already propagate `IsDateOnly`. Replace `DateOnlyEqualityRewriter` with a SQL-side `ScalarTemporalEqualityRewriter` that runs before `DateTimeEqualityRewriter`, uses a canonical URL allow-list initially containing birthdate, and rewrites only proven birthdate equality shapes. Add lightweight diagnostics that logs scalar temporal discovery and slow-query candidate metadata without logging raw search values.

**Tech Stack:** C#, .NET, xUnit, NSubstitute, existing FHIR search expression visitor pipeline, SQL Server search provider.

---

## Spec

Approved spec: `docs\superpowers\specs\2026-05-06-patient-birthdate-partial-date-query-design.md`

## File Structure

- Modify `src\Microsoft.Health.Fhir.Core\Models\SearchParameterInfo.cs`
  - Add `IsScalarTemporal` derived metadata next to `IsDateOnly`.
- Modify `src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\ISearchParameterSupportResolver.cs`
  - Extend the tuple return shape to include `IsScalarTemporal`.
- Modify `src\Microsoft.Health.Fhir.Shared.Core\Features\Search\Parameters\SearchParameterSupportResolver.cs`
  - Compute scalar temporal metadata from type-resolution results.
- Modify `src\Microsoft.Health.Fhir.Core\Features\Search\Registry\SearchParameterStatusManager.cs`
  - Propagate `IsScalarTemporal` on startup/status refresh and log scalar temporal discovery counts.
- Modify `src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\SearchParameterOperations.cs`
  - Propagate `IsScalarTemporal` for add/update/cache-refresh paths.
- Rename/replace:
  - Delete `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\DateOnlyEqualityRewriter.cs`
  - Create `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalEqualityRewriter.cs`
  - Delete `src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\DateOnlyEqualityRewriterTests.cs`
  - Create `src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalEqualityRewriterTests.cs`
- Create `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalSearchParameterDiagnostics.cs`
  - Collect non-PHI scalar temporal candidate metadata from expression trees for logging.
- Modify `src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs`
  - Replace the branch-local rewriter in the chain, add diagnostics before execution, and enrich long-running SQL logs.
- Modify tests:
   - `src\Microsoft.Health.Fhir.Core.UnitTests\Models\SearchParameterInfoIsDateOnlyTests.cs`
   - `src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Search\SearchParameters\SearchParameterSupportResolverTests.cs`
   - `src\Microsoft.Health.Fhir.Core.UnitTests\Features\Search\Registry\SearchParameterStatusManagerTests.cs`
   - `test\Microsoft.Health.Fhir.Shared.Tests.E2E\Rest\Search\DateSearchTests.cs`

## Commands

Use these targeted test commands throughout:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterInfoIsDateOnlyTests|FullyQualifiedName~SearchParameterStatusManagerTests"
dotnet test "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterSupportResolverTests"
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporalEqualityRewriterTests"
```

Use this broader verification before final handoff:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameter"
dotnet test "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterSupportResolverTests"
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporal|FullyQualifiedName~DateTimeEqualityRewriter"
```

---

### Task 1: Add scalar temporal metadata to core models and resolver contract

**Files:**
- Modify: `src\Microsoft.Health.Fhir.Core\Models\SearchParameterInfo.cs:118-128`
- Modify: `src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\ISearchParameterSupportResolver.cs:12-30`
- Modify: `src\Microsoft.Health.Fhir.Core.UnitTests\Models\SearchParameterInfoIsDateOnlyTests.cs`

- [ ] **Step 1: Write failing model tests**

Add these tests to `SearchParameterInfoIsDateOnlyTests`:

```csharp
[Fact]
public void GivenANewSearchParameterInfo_WhenConstructed_ThenIsScalarTemporalDefaultsToFalse()
{
    var sp = new SearchParameterInfo(
        "birthdate",
        "birthdate",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
        expression: "Patient.birthDate",
        baseResourceTypes: new[] { "Patient" });

    Assert.False(sp.IsScalarTemporal);
}

[Fact]
public void GivenSearchParameterInfo_WhenIsScalarTemporalToggles_ThenCalculateSearchParameterHashIsUnchanged()
{
    var sp = new SearchParameterInfo(
        "birthdate",
        "birthdate",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
        expression: "Patient.birthDate",
        baseResourceTypes: new[] { "Patient" })
    {
        SearchParameterStatus = SearchParameterStatus.Enabled,
    };

    string hashBefore = new List<SearchParameterInfo> { sp }.CalculateSearchParameterHash();

    sp.IsScalarTemporal = true;

    string hashAfter = new List<SearchParameterInfo> { sp }.CalculateSearchParameterHash();

    Assert.Equal(hashBefore, hashAfter);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterInfoIsDateOnlyTests"
```

Expected: fails because `SearchParameterInfo` does not contain `IsScalarTemporal`.

- [ ] **Step 3: Add `IsScalarTemporal` to `SearchParameterInfo`**

Add this property immediately after `IsDateOnly`:

```csharp
/// <summary>
/// Returns true if every type-resolution result for this parameter's expression is a scalar temporal FHIR node
/// (<c>date</c>, <c>dateTime</c>, or <c>instant</c>) and none is an explicit range-capable temporal type such as
/// <c>Period</c> or <c>Timing</c>. Set by
/// <see cref="Microsoft.Health.Fhir.Core.Features.Search.Parameters.SearchParameterSupportResolver"/>.
/// Used by SQL-side diagnostics and allow-listed query optimizations.
/// Derived metadata: NOT included in <see cref="SearchParameterInfoExtensions.CalculateSearchParameterHash"/>.
/// Defaults to <c>false</c>; a missing flag only forfeits diagnostics/optimization, never produces incorrect results.
/// </summary>
public bool IsScalarTemporal { get; set; }
```

- [ ] **Step 4: Extend `ISearchParameterSupportResolver` contract**

Change the return documentation and signature to include `IsScalarTemporal`:

```csharp
/// <para>
/// <c>IsScalarTemporal</c> — every type-resolution result for the parameter's expression has scalar temporal
/// FhirNodeType <c>date</c>, <c>dateTime</c>, or <c>instant</c>, with no <c>Period</c>, <c>Timing</c>, or other
/// range-capable temporal type. Used for SQL-side diagnostics and allow-listed query optimizations.
/// </para>
```

```csharp
(bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) IsSearchParameterSupported(SearchParameterInfo parameterInfo);
```

- [ ] **Step 5: Run compile-targeted test to surface tuple call-site errors**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterInfoIsDateOnlyTests"
```

Expected: model tests may pass compilation locally, but other projects will later fail until resolver consumers return/read the four-value tuple.

- [ ] **Step 6: Commit**

```powershell
git add "src\Microsoft.Health.Fhir.Core\Models\SearchParameterInfo.cs" "src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\ISearchParameterSupportResolver.cs" "src\Microsoft.Health.Fhir.Core.UnitTests\Models\SearchParameterInfoIsDateOnlyTests.cs"
git commit -m "feat(search): add scalar temporal search parameter metadata" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Compute scalar temporal metadata in `SearchParameterSupportResolver`

**Files:**
- Modify: `src\Microsoft.Health.Fhir.Shared.Core\Features\Search\Parameters\SearchParameterSupportResolver.cs:32-110`
- Modify: `src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Search\SearchParameters\SearchParameterSupportResolverTests.cs`

- [ ] **Step 1: Update existing tests for four-value tuple**

In existing tests, add assertions for `IsScalarTemporal`:

```csharp
Assert.False(supported.IsScalarTemporal);
```

Use `Assert.True(result.IsScalarTemporal);` in `GivenADateOnlyParameter_WhenResolvingSupport_ThenIsDateOnlyIsTrue`.

- [ ] **Step 2: Add failing scalar temporal resolver tests**

Append these tests to `SearchParameterSupportResolverTests`:

```csharp
[Fact]
public void GivenAScalarDateTimeParameter_WhenResolvingSupport_ThenIsScalarTemporalIsTrue()
{
    var sp = new SearchParameterInfo(
        "MedicationRequest-authoredon",
        "authoredon",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/MedicationRequest-authoredon"),
        expression: "MedicationRequest.authoredOn",
        baseResourceTypes: new[] { "MedicationRequest" });

    var result = _resolver.IsSearchParameterSupported(sp);

    Assert.True(result.Supported);
    Assert.False(result.IsDateOnly);
    Assert.True(result.IsScalarTemporal);
}

[Fact]
public void GivenAnInstantParameter_WhenResolvingSupport_ThenIsScalarTemporalIsTrue()
{
    var sp = new SearchParameterInfo(
        "Bundle-timestamp",
        "timestamp",
        SearchParamType.Date,
        new Uri("http://example.org/SearchParameter/Bundle-timestamp"),
        expression: "Bundle.timestamp",
        baseResourceTypes: new[] { "Bundle" });

    var result = _resolver.IsSearchParameterSupported(sp);

    Assert.True(result.Supported);
    Assert.False(result.IsDateOnly);
    Assert.True(result.IsScalarTemporal);
}

[Fact]
public void GivenAPeriodParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
{
    var sp = new SearchParameterInfo(
        "Encounter-date",
        "date",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
        expression: "Encounter.period",
        baseResourceTypes: new[] { "Encounter" });

    var result = _resolver.IsSearchParameterSupported(sp);

    Assert.True(result.Supported);
    Assert.False(result.IsDateOnly);
    Assert.False(result.IsScalarTemporal);
}

[Fact]
public void GivenAMixedTemporalParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
{
    var sp = new SearchParameterInfo(
        "Observation-date",
        "date",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
        expression: "Observation.effective",
        baseResourceTypes: new[] { "Observation" });

    var result = _resolver.IsSearchParameterSupported(sp);

    Assert.True(result.Supported);
    Assert.False(result.IsDateOnly);
    Assert.False(result.IsScalarTemporal);
}

[Fact]
public void GivenACompositeDateParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
{
    var birthdate = new SearchParameterInfo(
        "birthdate",
        "birthdate",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
        expression: "Patient.birthDate",
        baseResourceTypes: new[] { "Patient" });

    var composite = new SearchParameterInfo(
        "Patient-code-birthdate",
        "code-birthdate",
        SearchParamType.Composite,
        new Uri("http://example.org/SearchParameter/Patient-code-birthdate"),
        components: new[]
        {
            new SearchParameterComponentInfo(
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                "birthDate")
            {
                ResolvedSearchParameter = birthdate,
            },
        },
        expression: "Patient",
        baseResourceTypes: new[] { "Patient" });

    var result = _resolver.IsSearchParameterSupported(composite);

    Assert.True(result.Supported);
    Assert.False(result.IsDateOnly);
    Assert.False(result.IsScalarTemporal);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterSupportResolverTests"
```

Expected: compile fails or assertions fail because resolver still returns only three tuple fields and does not compute `IsScalarTemporal`.

- [ ] **Step 4: Implement scalar temporal computation**

Update `SearchParameterSupportResolver.IsSearchParameterSupported` with these changes:

```csharp
public (bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) IsSearchParameterSupported(SearchParameterInfo parameterInfo)
{
    EnsureArg.IsNotNull(parameterInfo, nameof(parameterInfo));

    if (string.IsNullOrWhiteSpace(parameterInfo.Expression))
    {
        return (false, false, false, false);
    }

    Expression parsed = _compiler.Parse(parameterInfo.Expression);
    if (parameterInfo.Component != null && parameterInfo.Component.Any(x => x.ResolvedSearchParameter == null))
    {
        return (false, false, false, false);
    }

    (SearchParamType Type, Expression, Uri DefinitionUrl)[] componentExpressions = parameterInfo.Component
        ?.Select(x => (x.ResolvedSearchParameter.Type,
            _compiler.Parse(x.Expression),
            x.DefinitionUrl))
        .ToArray();

    List<string> resourceTypes = (parameterInfo.TargetResourceTypes ?? Enumerable.Empty<string>()).Concat(parameterInfo.BaseResourceTypes ?? Enumerable.Empty<string>()).ToList();

    if (!resourceTypes.Any())
    {
        throw new NotSupportedException("No target resources defined.");
    }

    bool isSimpleDateParameter =
        parameterInfo.Type == SearchParamType.Date &&
        (parameterInfo.Component == null || parameterInfo.Component.Count == 0);

    bool allResolutionsAreDateOnly = isSimpleDateParameter;
    bool allResolutionsAreScalarTemporal = isSimpleDateParameter;

    foreach (var resource in resourceTypes)
    {
        SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
            resource,
            (parameterInfo.Type, parsed, parameterInfo.Url),
            componentExpressions).ToArray();

        if (allResolutionsAreDateOnly)
        {
            if (results.Length == 0 ||
                !results.All(r => string.Equals(r.FhirNodeType, "date", StringComparison.OrdinalIgnoreCase)))
            {
                allResolutionsAreDateOnly = false;
            }
        }

        if (allResolutionsAreScalarTemporal)
        {
            if (results.Length == 0 ||
                !results.All(r => IsScalarTemporalNodeType(r.FhirNodeType)))
            {
                allResolutionsAreScalarTemporal = false;
            }
        }

        var converters = results
            .Select(result => (
                result,
                hasConverter: _searchValueConverterManager.TryGetConverter(
                    GetBaseType(result.ClassMapping),
                    TypedElementSearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                    out ITypedElementToSearchValueConverter converter),
                converter))
            .ToArray();

        if (!converters.Any())
        {
            return (false, false, false, false);
        }

        if (!converters.All(x => x.hasConverter))
        {
            bool partialSupport = converters.Any(x => x.hasConverter);
            return (partialSupport, partialSupport, false, false);
        }
    }

    string GetBaseType(ClassMapping classMapping)
    {
        return classMapping.IsCodeOfT ? _codeOfTName : classMapping.Name;
    }

    static bool IsScalarTemporalNodeType(string fhirNodeType)
    {
        return string.Equals(fhirNodeType, "date", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fhirNodeType, "dateTime", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fhirNodeType, "instant", StringComparison.OrdinalIgnoreCase);
    }

    return (true, false, allResolutionsAreDateOnly, allResolutionsAreScalarTemporal);
}
```

- [ ] **Step 5: Run resolver tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterSupportResolverTests"
```

Expected: all `SearchParameterSupportResolverTests` pass.

- [ ] **Step 6: Commit**

```powershell
git add "src\Microsoft.Health.Fhir.Shared.Core\Features\Search\Parameters\SearchParameterSupportResolver.cs" "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Search\SearchParameters\SearchParameterSupportResolverTests.cs"
git commit -m "feat(search): compute scalar temporal search metadata" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Propagate metadata through search parameter lifecycle

**Files:**
- Modify: `src\Microsoft.Health.Fhir.Core\Features\Search\Registry\SearchParameterStatusManager.cs:119-127`
- Modify: `src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\SearchParameterOperations.cs:109-117,230-238,452-465`
- Modify: `src\Microsoft.Health.Fhir.Core.UnitTests\Features\Search\Registry\SearchParameterStatusManagerTests.cs:121-131`

- [ ] **Step 1: Update mock tuple returns in tests**

In `SearchParameterStatusManagerTests`, change tuple returns from three values to four values:

```csharp
_searchParameterSupportResolver
    .IsSearchParameterSupported(Arg.Any<SearchParameterInfo>())
    .Returns((false, false, false, false));

_searchParameterSupportResolver
    .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[4]))
    .Returns((true, false, false, true));

_searchParameterSupportResolver
    .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[5]))
    .Returns((true, false, false, false));
```

Add assertions after the existing `IsDateOnly` / support assertions:

```csharp
Assert.True(list[4].IsScalarTemporal);
Assert.False(list[5].IsScalarTemporal);
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterStatusManagerTests"
```

Expected: compile fails because production call sites still expect the three-field tuple, or assertions fail because `IsScalarTemporal` is not assigned.

- [ ] **Step 3: Propagate in `SearchParameterStatusManager`**

Replace the tuple declaration and assignments:

```csharp
(bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) supportedResult = CheckSearchParameterSupport(p);
p.IsSupported = supportedResult.Supported;
p.IsPartiallySupported = supportedResult.IsPartiallySupported;
p.IsDateOnly = supportedResult.IsDateOnly;
p.IsScalarTemporal = supportedResult.IsScalarTemporal;
```

If `CheckSearchParameterSupport` has an explicit tuple return type elsewhere in the file, update it to:

```csharp
private (bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) CheckSearchParameterSupport(SearchParameterInfo parameterInfo)
```

- [ ] **Step 4: Add startup observability counts in `SearchParameterStatusManager`**

After all `updated` search parameters are collected and before publishing notifications, add:

```csharp
int scalarTemporalCount = _searchParameterDefinitionManager.AllSearchParameters.Count(p => p.IsScalarTemporal);
int scalarTemporalAllowListedCount = _searchParameterDefinitionManager.AllSearchParameters.Count(IsScalarTemporalRewriteAllowListed);

_logger.LogInformation(
    "SearchParameterStatusManager: Scalar temporal search parameters discovered. Total={ScalarTemporalCount}, AllowListed={ScalarTemporalAllowListedCount}, NotAllowListed={ScalarTemporalNotAllowListedCount}",
    scalarTemporalCount,
    scalarTemporalAllowListedCount,
    scalarTemporalCount - scalarTemporalAllowListedCount);
```

Add this private helper in the same class:

```csharp
private static bool IsScalarTemporalRewriteAllowListed(SearchParameterInfo parameterInfo)
{
    return parameterInfo.IsScalarTemporal &&
           parameterInfo.Url != null &&
           string.Equals(
               parameterInfo.Url.OriginalString,
               "http://hl7.org/fhir/SearchParameter/individual-birthdate",
               StringComparison.Ordinal);
}
```

- [ ] **Step 5: Propagate in `SearchParameterOperations` add/update/cache refresh**

Update the add path:

```csharp
(bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) supportedResult = _searchParameterSupportResolver.IsSearchParameterSupported(searchParameterInfo);
```

```csharp
searchParameterInfo.IsDateOnly = supportedResult.IsDateOnly;
searchParameterInfo.IsScalarTemporal = supportedResult.IsScalarTemporal;
```

Update the update path with the same tuple type and assignment.

Rename `ApplyIsDateOnlyToNewParams` to `ApplyDerivedTemporalMetadataToNewParams`, update both call sites, and replace the body assignment:

```csharp
var result = _searchParameterSupportResolver.IsSearchParameterSupported(info);
info.IsDateOnly = result.IsDateOnly;
info.IsScalarTemporal = result.IsScalarTemporal;
```

Update the warning message:

```csharp
_logger.LogWarning(ex, "Failed to resolve derived temporal metadata for search parameter '{Url}' during cache refresh.", url);
```

- [ ] **Step 6: Run lifecycle tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterStatusManagerTests"
```

Expected: tests pass.

- [ ] **Step 7: Commit**

```powershell
git add "src\Microsoft.Health.Fhir.Core\Features\Search\Registry\SearchParameterStatusManager.cs" "src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\SearchParameterOperations.cs" "src\Microsoft.Health.Fhir.Core.UnitTests\Features\Search\Registry\SearchParameterStatusManagerTests.cs"
git commit -m "feat(search): propagate scalar temporal metadata" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Replace the branch-local date-only rewriter with scalar temporal rewrite tests

**Files:**
- Delete: `src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\DateOnlyEqualityRewriterTests.cs`
- Create: `src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalEqualityRewriterTests.cs`
- Later task creates implementation in `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalEqualityRewriter.cs`

- [ ] **Step 1: Replace the test file**

Delete `DateOnlyEqualityRewriterTests.cs` and create `ScalarTemporalEqualityRewriterTests.cs` with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ScalarTemporalEqualityRewriterTests
    {
        private static readonly DateTimeOffset StartOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfMonth = new DateTimeOffset(2016, 7, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfMonth = new DateTimeOffset(2016, 7, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfYear = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfYear = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

        private static SearchParameterInfo BuildParam(bool isScalarTemporal, Uri url = null)
        {
            return new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                url ?? new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsDateOnly = isScalarTemporal,
                IsScalarTemporal = isScalarTemporal,
            };
        }

        private static MultiaryExpression EqualityPattern(DateTimeOffset start, DateTimeOffset end) =>
            (MultiaryExpression)Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, end));

        [Fact]
        public void GivenAllowListedScalarTemporalExactDay_WhenRewritten_ThenCollapsedToSingleEndDateTimeEquality()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfDay, EndOfDay));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var binary = Assert.IsType<BinaryExpression>(rewritten.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(EndOfDay, binary.Value);
        }

        [Fact]
        public void GivenAllowListedScalarTemporalMonth_WhenRewritten_ThenCollapsedToEndDateTimeRange()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfMonth, EndOfMonth));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var multiary = Assert.IsType<MultiaryExpression>(rewritten.Expression);
            Assert.Equal(MultiaryOperator.And, multiary.MultiaryOperation);
            Assert.Collection(
                multiary.Expressions,
                first =>
                {
                    var binary = Assert.IsType<BinaryExpression>(first);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.GreaterThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(StartOfMonth, binary.Value);
                },
                second =>
                {
                    var binary = Assert.IsType<BinaryExpression>(second);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.LessThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(EndOfMonth, binary.Value);
                });
        }

        [Fact]
        public void GivenAllowListedScalarTemporalYear_WhenRewritten_ThenCollapsedToEndDateTimeRange()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfYear, EndOfYear));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var multiary = Assert.IsType<MultiaryExpression>(rewritten.Expression);
            Assert.Equal(MultiaryOperator.And, multiary.MultiaryOperation);
            Assert.Collection(
                multiary.Expressions,
                first =>
                {
                    var binary = Assert.IsType<BinaryExpression>(first);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.GreaterThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(StartOfYear, binary.Value);
                },
                second =>
                {
                    var binary = Assert.IsType<BinaryExpression>(second);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.LessThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(EndOfYear, binary.Value);
                });
        }

        [Fact]
        public void GivenScalarTemporalParameterNotAllowListed_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(
                BuildParam(isScalarTemporal: true, new Uri("http://example.org/SearchParameter/test-date")),
                EqualityPattern(StartOfYear, EndOfYear));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenNonScalarTemporalAllowListedParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: false), EqualityPattern(StartOfYear, EndOfYear));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenSingleSidedPredicate_WhenRewritten_ThenPassThrough()
        {
            var single = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), single);

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenRangeOperatorPattern_WhenRewritten_ThenPassThrough()
        {
            var range = Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), range);

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenApproximateExpression_WhenRewritten_ThenPassThrough()
        {
            var approxStart = StartOfDay.AddDays(-30);
            var approxEnd = EndOfDay.AddDays(30);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(approxStart, approxEnd));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenCompositeParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var composite = new SearchParameterInfo(
                "Observation-code-value-date",
                "code-value-date",
                SearchParamType.Composite,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-date"),
                expression: "Observation",
                baseResourceTypes: new[] { "Observation" })
            {
                IsScalarTemporal = true,
            };
            var expr = new SearchParameterExpression(composite, EqualityPattern(StartOfDay, EndOfDay));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporalEqualityRewriterTests"
```

Expected: fails because `ScalarTemporalEqualityRewriter` does not exist.

- [ ] **Step 3: Commit failing tests**

```powershell
git add "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\DateOnlyEqualityRewriterTests.cs" "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalEqualityRewriterTests.cs"
git commit -m "test(sql): cover scalar temporal equality rewrite" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Implement `ScalarTemporalEqualityRewriter` and wire the rewrite chain

**Files:**
- Delete: `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\DateOnlyEqualityRewriter.cs`
- Create: `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalEqualityRewriter.cs`
- Modify: `src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs:2061-2067`

- [ ] **Step 1: Create `ScalarTemporalEqualityRewriter`**

Create `ScalarTemporalEqualityRewriter.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites allow-listed scalar temporal equality predicates before the generic date/time equality rewriter.
    /// </summary>
    internal class ScalarTemporalEqualityRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly ScalarTemporalEqualityRewriter Instance = new ScalarTemporalEqualityRewriter();

        private static readonly ISet<string> AllowListedUrls = new HashSet<string>(StringComparer.Ordinal)
        {
            "http://hl7.org/fhir/SearchParameter/individual-birthdate",
        };

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (!IsActivatedScalarTemporalParameter(expression))
            {
                return expression;
            }

            if (!TryMatchEqualityPattern(expression.Expression, out BinaryExpression startGe, out BinaryExpression endLe))
            {
                return expression;
            }

            if (startGe.Value is not DateTimeOffset startValue || endLe.Value is not DateTimeOffset endValue)
            {
                return expression;
            }

            if (IsExactCalendarDay(startValue, endValue))
            {
                var collapsed = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, endLe.ComponentIndex, endLe.Value);
                return new SearchParameterExpression(expression.Parameter, collapsed);
            }

            if (IsFullCalendarMonth(startValue, endValue) || IsFullCalendarYear(startValue, endValue))
            {
                var collapsed = Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, endLe.ComponentIndex, startValue),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, endLe.ComponentIndex, endValue));

                return new SearchParameterExpression(expression.Parameter, collapsed);
            }

            return expression;
        }

        internal static bool IsActivatedScalarTemporalParameter(SearchParameterExpression expression)
        {
            return expression?.Parameter != null &&
                   expression.Parameter.Type == SearchParamType.Date &&
                   expression.Parameter.IsScalarTemporal &&
                   expression.Parameter.Component == null &&
                   expression.Parameter.Url != null &&
                   AllowListedUrls.Contains(expression.Parameter.Url.OriginalString);
        }

        internal static bool TryMatchEqualityPattern(Expression expression, out BinaryExpression startGe, out BinaryExpression endLe)
        {
            startGe = null;
            endLe = null;

            if (expression is not MultiaryExpression multiary ||
                multiary.MultiaryOperation != MultiaryOperator.And ||
                multiary.Expressions.Count != 2 ||
                multiary.Expressions[0] is not BinaryExpression first ||
                multiary.Expressions[1] is not BinaryExpression second ||
                first.ComponentIndex != second.ComponentIndex)
            {
                return false;
            }

            if (first.FieldName == FieldName.DateTimeStart && first.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                second.FieldName == FieldName.DateTimeEnd && second.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = first;
                endLe = second;
                return true;
            }

            if (second.FieldName == FieldName.DateTimeStart && second.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                first.FieldName == FieldName.DateTimeEnd && first.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = second;
                endLe = first;
                return true;
            }

            return false;
        }

        private static bool IsExactCalendarDay(DateTimeOffset startValue, DateTimeOffset endValue)
        {
            return startValue.Offset == TimeSpan.Zero &&
                   endValue.Offset == TimeSpan.Zero &&
                   startValue.TimeOfDay == TimeSpan.Zero &&
                   endValue == startValue.AddDays(1).AddTicks(-1);
        }

        private static bool IsFullCalendarMonth(DateTimeOffset startValue, DateTimeOffset endValue)
        {
            return startValue.Offset == TimeSpan.Zero &&
                   endValue.Offset == TimeSpan.Zero &&
                   startValue.TimeOfDay == TimeSpan.Zero &&
                   startValue.Day == 1 &&
                   endValue == startValue.AddMonths(1).AddTicks(-1);
        }

        private static bool IsFullCalendarYear(DateTimeOffset startValue, DateTimeOffset endValue)
        {
            return startValue.Offset == TimeSpan.Zero &&
                   endValue.Offset == TimeSpan.Zero &&
                   startValue.TimeOfDay == TimeSpan.Zero &&
                   startValue.Month == 1 &&
                   startValue.Day == 1 &&
                   endValue == startValue.AddYears(1).AddTicks(-1);
        }
    }
}
```

- [ ] **Step 2: Delete `DateOnlyEqualityRewriter.cs`**

Remove `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\DateOnlyEqualityRewriter.cs`.

- [ ] **Step 3: Wire the replacement before `DateTimeEqualityRewriter`**

In `SqlServerSearchService.CreateDefaultSearchExpression`, replace:

```csharp
.AcceptVisitor(DateOnlyEqualityRewriter.Instance)
.AcceptVisitor(DateTimeEqualityRewriter.Instance)
```

with:

```csharp
.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance)
.AcceptVisitor(DateTimeEqualityRewriter.Instance)
```

- [ ] **Step 4: Run rewriter tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporalEqualityRewriterTests"
```

Expected: `ScalarTemporalEqualityRewriterTests` pass.

- [ ] **Step 5: Run generic date/time rewrite tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~DateTimeEqualityRewriterTests"
```

Expected: existing generic `DateTimeEqualityRewriterTests` pass, confirming the replacement does not alter generic Core behavior.

- [ ] **Step 6: Commit**

```powershell
git add "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\DateOnlyEqualityRewriter.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalEqualityRewriter.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs"
git commit -m "feat(sql): replace date-only rewrite with scalar temporal rewrite" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Add scalar temporal diagnostics and slow-query observability

**Files:**
- Create: `src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalSearchParameterDiagnostics.cs`
- Modify: `src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs:425-435,827-849,1145-1251`
- Create or modify tests in `src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalSearchParameterDiagnosticsTests.cs`

- [ ] **Step 1: Write failing diagnostics tests**

Create `ScalarTemporalSearchParameterDiagnosticsTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ScalarTemporalSearchParameterDiagnosticsTests
    {
        [Fact]
        public void GivenScalarTemporalEqualityNotAllowListed_WhenCollected_ThenCandidateIsReturned()
        {
            var parameter = new SearchParameterInfo(
                "test-date",
                "date",
                SearchParamType.Date,
                new Uri("http://example.org/SearchParameter/test-date"),
                expression: "MedicationRequest.authoredOn",
                baseResourceTypes: new[] { "MedicationRequest" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            var result = ScalarTemporalSearchParameterDiagnostics.Collect(expression);

            var candidate = Assert.Single(result);
            Assert.Equal("http://example.org/SearchParameter/test-date", candidate.Url);
            Assert.Equal("date", candidate.Code);
            Assert.True(candidate.IsScalarTemporal);
            Assert.False(candidate.IsAllowListed);
            Assert.True(candidate.HasEqualityShape);
        }

        [Fact]
        public void GivenAllowListedBirthdate_WhenCollected_ThenAllowListedIsTrue()
        {
            var parameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            var result = ScalarTemporalSearchParameterDiagnostics.Collect(expression);

            var candidate = Assert.Single(result);
            Assert.True(candidate.IsAllowListed);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporalSearchParameterDiagnosticsTests"
```

Expected: fails because diagnostics class does not exist.

- [ ] **Step 3: Implement diagnostics collector**

Create `ScalarTemporalSearchParameterDiagnostics.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal static class ScalarTemporalSearchParameterDiagnostics
    {
        internal static IReadOnlyList<ScalarTemporalSearchParameterDiagnostic> Collect(Expression expression)
        {
            var context = new List<ScalarTemporalSearchParameterDiagnostic>();
            expression?.AcceptVisitor(Collector.Instance, context);
            return context;
        }

        internal readonly struct ScalarTemporalSearchParameterDiagnostic
        {
            public ScalarTemporalSearchParameterDiagnostic(string url, string code, bool isScalarTemporal, bool isAllowListed, bool hasEqualityShape)
            {
                Url = url;
                Code = code;
                IsScalarTemporal = isScalarTemporal;
                IsAllowListed = isAllowListed;
                HasEqualityShape = hasEqualityShape;
            }

            public string Url { get; }

            public string Code { get; }

            public bool IsScalarTemporal { get; }

            public bool IsAllowListed { get; }

            public bool HasEqualityShape { get; }
        }

        private sealed class Collector : ExpressionRewriterWithInitialContext<List<ScalarTemporalSearchParameterDiagnostic>>
        {
            internal static readonly Collector Instance = new Collector();

            public override Expression VisitSearchParameter(SearchParameterExpression expression, List<ScalarTemporalSearchParameterDiagnostic> context)
            {
                if (expression.Parameter?.IsScalarTemporal == true)
                {
                    context.Add(new ScalarTemporalSearchParameterDiagnostic(
                        expression.Parameter.Url?.OriginalString,
                        expression.Parameter.Code,
                        expression.Parameter.IsScalarTemporal,
                        ScalarTemporalEqualityRewriter.IsActivatedScalarTemporalParameter(expression),
                        ScalarTemporalEqualityRewriter.TryMatchEqualityPattern(expression.Expression, out _, out _)));
                }

                return base.VisitSearchParameter(expression, context);
            }
        }
    }
}
```

- [ ] **Step 4: Capture diagnostics in `SearchImpl`**

Immediately after:

```csharp
Expression searchExpression = sqlSearchOptions.Expression;
```

add:

```csharp
IReadOnlyList<ScalarTemporalSearchParameterDiagnostics.ScalarTemporalSearchParameterDiagnostic> scalarTemporalDiagnostics =
    ScalarTemporalSearchParameterDiagnostics.Collect(searchExpression);

foreach (var candidate in scalarTemporalDiagnostics.Where(x => x.HasEqualityShape && !x.IsAllowListed))
{
    _logger.LogDebug(
        "Scalar temporal equality search parameter candidate is not allow-listed. Url={SearchParameterUrl}, Code={SearchParameterCode}",
        candidate.Url,
        candidate.Code);
}
```

Ensure `System.Linq` is already available in `SqlServerSearchService.cs`; add it if missing.

- [ ] **Step 5: Enrich long-running query logs**

Before the `Task.Run` block in the long-running SQL logging section, capture:

```csharp
string scalarTemporalDiagnosticSummary = BuildScalarTemporalDiagnosticSummary(scalarTemporalDiagnostics);
```

Update the call:

```csharp
await LogQueryStoreByTextAsync(
    queryTextSnapshot,
    isStoredProcSnapshot,
    scalarTemporalDiagnosticSummary,
    _logger,
    timeoutSnapshot,
    executionTimeSnapshot,
    loggingCts.Token);
```

Update `LogQueryStoreByTextAsync` signature:

```csharp
private async Task LogQueryStoreByTextAsync(
    string queryText,
    bool isStoredProcedure,
    string scalarTemporalDiagnosticSummary,
    ILogger logger,
    int timeoutSeconds,
    long executionTime,
    CancellationToken ct)
```

Update both `LogWarning` calls in that method to include the summary:

```csharp
logger.LogWarning(
    "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} ScalarTemporalSearchParameters={ScalarTemporalSearchParameters} QueryStoreStats={QueryStoreStats}",
    executionTime,
    queryText,
    scalarTemporalDiagnosticSummary,
    sb.ToString());
```

```csharp
logger.LogWarning(
    "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} ScalarTemporalSearchParameters={ScalarTemporalSearchParameters} QueryStoreStats={QueryStoreStats}",
    executionTime,
    queryText,
    scalarTemporalDiagnosticSummary,
    "No Query Store matches found.");
```

Add this helper near the other private static helpers:

```csharp
private static string BuildScalarTemporalDiagnosticSummary(IReadOnlyList<ScalarTemporalSearchParameterDiagnostics.ScalarTemporalSearchParameterDiagnostic> diagnostics)
{
    if (diagnostics == null || diagnostics.Count == 0)
    {
        return "none";
    }

    return string.Join(
        ";",
        diagnostics.Select(x =>
            $"url={x.Url ?? string.Empty},code={x.Code ?? string.Empty},allowListed={x.IsAllowListed},equality={x.HasEqualityShape}"));
}
```

- [ ] **Step 6: Log rewrite-fired events**

After `CreateDefaultSearchExpression` returns the rewritten expression in `SearchImpl`, collect rewritten diagnostics and log allow-listed equality candidates:

```csharp
foreach (var candidate in ScalarTemporalSearchParameterDiagnostics.Collect(expression).Where(x => x.IsAllowListed))
{
    _logger.LogDebug(
        "Scalar temporal equality rewrite active. Url={SearchParameterUrl}, Code={SearchParameterCode}",
        candidate.Url,
        candidate.Code);
}
```

Place this after the `SqlRootExpression expression = (SqlRootExpression)CreateDefaultSearchExpression(searchExpression, clonedSearchOptions)` assignment block in `SearchImpl` and before executing the SQL command.

- [ ] **Step 7: Run diagnostics tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporalSearchParameterDiagnosticsTests"
```

Expected: diagnostics tests pass.

- [ ] **Step 8: Commit**

```powershell
git add "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalSearchParameterDiagnostics.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs" "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalSearchParameterDiagnosticsTests.cs"
git commit -m "feat(sql): add scalar temporal search diagnostics" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: Add SQL E2E regression coverage for partial birthdate semantics

**Files:**
- Modify: `test\Microsoft.Health.Fhir.Shared.Tests.E2E\Rest\Search\DateSearchTests.cs:241-277`

- [ ] **Step 1: Write failing E2E test cases**

Add this test method below `GivenPatientsWithDateOnlyBirthdate_WhenSearchedByEqualityAndRange_ThenCorrectPatientsAreReturned`:

```csharp
[Fact]
[Trait(Traits.Priority, Priority.One)]
public async Task GivenPatientsWithPartialBirthdates_WhenSearchedByEquality_ThenContainmentSemanticsArePreserved()
{
    string tag = Guid.NewGuid().ToString();
    Patient[] patients = await Client.CreateResourcesAsync<Patient>(
        p => SetPatientBirthDate(p, "2000", tag),
        p => SetPatientBirthDate(p, "2000-03", tag),
        p => SetPatientBirthDate(p, "2000-03-03", tag),
        p => SetPatientBirthDate(p, "1999-12-31", tag),
        p => SetPatientBirthDate(p, "2000-04-01", tag));

    try
    {
        Bundle yearBundle = await Client.SearchAsync(ResourceType.Patient, $"birthdate=2000&_tag={tag}");
        ValidateBundle(yearBundle, patients[0], patients[1], patients[2], patients[4]);

        Bundle monthBundle = await Client.SearchAsync(ResourceType.Patient, $"birthdate=2000-03&_tag={tag}");
        ValidateBundle(monthBundle, patients[1], patients[2]);

        Bundle dayBundle = await Client.SearchAsync(ResourceType.Patient, $"birthdate=2000-03-03&_tag={tag}");
        ValidateBundle(dayBundle, patients[2]);
    }
    finally
    {
        await Client.DeleteResourcesAsync(patients);
    }
}
```

This creates patients with:

```json
{ "resourceType": "Patient", "birthDate": "2000" }
{ "resourceType": "Patient", "birthDate": "2000-03" }
{ "resourceType": "Patient", "birthDate": "2000-03-03" }
{ "resourceType": "Patient", "birthDate": "1999-12-31" }
{ "resourceType": "Patient", "birthDate": "2000-04-01" }
```

Assert these searches:

```text
Patient?birthdate=2000
```

returns the `2000`, `2000-03`, and `2000-03-03` patients, and not `1999-12-31`.

It also returns `2000-04-01`, because day precision is fully contained by the year query range.

```text
Patient?birthdate=2000-03
```

returns the `2000-03` and `2000-03-03` patients, and not the broader `2000` or `2000-04-01` patients.

```text
Patient?birthdate=2000-03-03
```

returns only the exact-day patient.

- [ ] **Step 2: Run the focused E2E test and record baseline**

Run:

```powershell
dotnet test "test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj" --filter "FullyQualifiedName~DateSearchTests.GivenPatientsWithPartialBirthdates_WhenSearchedByEquality_ThenContainmentSemanticsArePreserved"
```

Expected before implementation: this may pass semantically because the existing predicates are correct, but it will protect against rewrite regressions. If it requires a local SQL E2E environment and cannot run locally, record the command and continue with unit-test verification.

- [ ] **Step 3: Commit**

```powershell
git add "test\Microsoft.Health.Fhir.Shared.Tests.E2E\Rest\Search\DateSearchTests.cs"
git commit -m "test(e2e): cover partial birthdate equality semantics" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 8: Final integration verification and cleanup

**Files:**
- Review all changed files.
- Do not modify `src\Microsoft.Health.Fhir.SqlServer\Features\Schema\Migrations\113.sql` unless it belongs to this work; it was already untracked before planning.

- [ ] **Step 1: Run targeted unit tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterInfoIsDateOnlyTests|FullyQualifiedName~SearchParameterStatusManagerTests|FullyQualifiedName~DateTimeEqualityRewriterTests"
dotnet test "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameterSupportResolverTests"
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~ScalarTemporal"
```

Expected: all targeted tests pass.

- [ ] **Step 2: Run broader affected tests**

Run:

```powershell
dotnet test "src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj" --filter "FullyQualifiedName~SearchParameter"
dotnet test "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj" --filter "FullyQualifiedName~Search"
```

Expected: all affected tests pass.

- [ ] **Step 3: Inspect git status**

Run:

```powershell
git --no-pager status --short
```

Expected: only intentional files for this feature are modified/staged. The pre-existing untracked `src\Microsoft.Health.Fhir.SqlServer\Features\Schema\Migrations\113.sql` may still appear and should not be included unless the user confirms it belongs to this feature.

- [ ] **Step 4: Final commit if any verification-only fixes were needed**

If Step 1 or Step 2 required code fixes after the last task commit:

```powershell
git add "src\Microsoft.Health.Fhir.Core\Models\SearchParameterInfo.cs" "src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\ISearchParameterSupportResolver.cs" "src\Microsoft.Health.Fhir.Shared.Core\Features\Search\Parameters\SearchParameterSupportResolver.cs" "src\Microsoft.Health.Fhir.Core\Features\Search\Registry\SearchParameterStatusManager.cs" "src\Microsoft.Health.Fhir.Core\Features\Search\Parameters\SearchParameterOperations.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\DateOnlyEqualityRewriter.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalEqualityRewriter.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\Expressions\Visitors\ScalarTemporalSearchParameterDiagnostics.cs" "src\Microsoft.Health.Fhir.SqlServer\Features\Search\SqlServerSearchService.cs" "src\Microsoft.Health.Fhir.Core.UnitTests\Models\SearchParameterInfoIsDateOnlyTests.cs" "src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Search\SearchParameters\SearchParameterSupportResolverTests.cs" "src\Microsoft.Health.Fhir.Core.UnitTests\Features\Search\Registry\SearchParameterStatusManagerTests.cs" "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\DateOnlyEqualityRewriterTests.cs" "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalEqualityRewriterTests.cs" "src\Microsoft.Health.Fhir.SqlServer.UnitTests\Features\Search\Expressions\ScalarTemporalSearchParameterDiagnosticsTests.cs" "test\Microsoft.Health.Fhir.Shared.Tests.E2E\Rest\Search\DateSearchTests.cs"
git commit -m "fix(search): finalize scalar temporal birthdate optimization" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

If no fixes were needed, do not create an empty commit.

---

## Self-Review Notes

- Spec coverage:
  - Metadata: Tasks 1-3.
  - Birthdate allow-list: Tasks 4-6.
  - Replacement of branch-local `DateOnlyEqualityRewriter`: Tasks 4-5.
  - Query chain placement before `DateTimeEqualityRewriter`: Task 5.
  - Observability and slow-query candidate tracking: Tasks 3 and 6.
  - Partial-date semantics: Tasks 4, 5, and 7.
- No schema changes are planned.
- No raw search values should be added to new logs; diagnostic logs use URL/code and query hash/query text already present in existing long-running SQL logging.
