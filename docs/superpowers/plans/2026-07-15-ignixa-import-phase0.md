# Ignixa Import Phase 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in Ignixa implementation of `$import` parsing while preserving Firely as the default and keeping the existing import, indexing, SQL, and Cosmos boundaries unchanged.

**Architecture:** Keep the existing `IImportResourceParser` contract as the only migrated seam. Four version-specific Firely provider projects share the current parser source, while one net9-only Ignixa provider selects its generated schema at runtime. The API composition root registers exactly one implementation from a global `Firely|Ignixa` setting; both implementations return the existing `ImportResource` and `ResourceWrapper`.

**Tech Stack:** .NET 8/9, C#, xUnit, NSubstitute, Firely SDK 5.x, Ignixa serialization/specification packages, Microsoft.Health dependency-injection extensions.

**Design reference:** `docs/superpowers/specs/2026-07-15-ignixa-incremental-integration-design.md`

---

## Scope Check

This plan implements Phase 0 only. Export, persistence codecs, FHIRPath, search indexing, HTTP formatters, validation, bundles, PATCH, conformance, terminology, and XML require separate plans and PRs.

## File Map

### Core configuration

- Create `src/Microsoft.Health.Fhir.Core/Configs/FhirSdkProvider.cs`: two-state provider enum.
- Modify `src/Microsoft.Health.Fhir.Core/Configs/CoreFeatureConfiguration.cs`: defaulted provider property.
- Create `src/Microsoft.Health.Fhir.Core.UnitTests/Config/FhirSdkProviderConfigurationTests.cs`: default and explicit-value tests.

### Firely providers

- Create `src/Microsoft.Health.Fhir.Core/Features/Operations/Import/ImportResourceIdValidator.cs`: provider-neutral import ID policy and stable error.
- Create `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Operations/Import/ImportResourceIdValidatorTests.cs`: shared policy tests.
- Create `src/Microsoft.Health.Fhir.FirelySdk/Features/Operations/Import/FirelyImportResourceParser.cs`: one shared source implementation containing current behavior.
- Create four provider project files:
  - `src/Microsoft.Health.Fhir.Stu3.FirelySdk/Microsoft.Health.Fhir.Stu3.FirelySdk.csproj`
  - `src/Microsoft.Health.Fhir.R4.FirelySdk/Microsoft.Health.Fhir.R4.FirelySdk.csproj`
  - `src/Microsoft.Health.Fhir.R4B.FirelySdk/Microsoft.Health.Fhir.R4B.FirelySdk.csproj`
  - `src/Microsoft.Health.Fhir.R5.FirelySdk/Microsoft.Health.Fhir.R5.FirelySdk.csproj`
- Delete `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Import/ImportResourceParser.cs`.
- Modify `src/Microsoft.Health.Fhir.Shared.Core/Microsoft.Health.Fhir.Shared.Core.projitems`: stop compiling the old parser.

### Ignixa provider

- Create `src/Microsoft.Health.Fhir.Ignixa/Microsoft.Health.Fhir.Ignixa.csproj`: net9-only minimal integration project.
- Create `src/Microsoft.Health.Fhir.Ignixa/IgnixaSchemaContext.cs`: current-version generated schema selection.
- Create `src/Microsoft.Health.Fhir.Ignixa/Features/Operations/Import/IgnixaImportResourceParser.cs`: Ignixa parser and raw JSON mutations.
- Modify `Directory.Packages.props`: pin only the four Ignixa packages needed by import.

### Composition and diagnostics

- Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/OperationsModule.cs`: select exactly one parser.
- Create `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirSdkProviderStartupLogger.cs`: log configured provider and migrated seams.
- Modify `src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems`: compile the startup logger.
- Create `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/OperationsModuleTests.cs`: registration and unsupported-TFM tests.
- Modify `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems`: compile module tests.

### Provider behavior tests

- Modify `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/ImportResourceParserTests.cs`: Firely behavior tests after extraction.
- Create `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/ImportResourceParserParityTests.cs`: net9 Firely/Ignixa parity corpus.
- Modify `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems`: compile parity tests.

### Project and build wiring

- Modify all four version API projects to reference their Firely provider and conditionally reference Ignixa on net9.
- Modify all four version Core unit-test projects with matching provider references.
- Modify `Microsoft.Health.Fhir.sln`, `R4.slnf`, and `R5.slnf`.
- Modify `build/docker/Dockerfile`: copy provider project files before restore.
- Modify `src/Microsoft.Health.Fhir.Shared.Web/appsettings.json`: explicitly select Firely.

---

### Task 1: Add the provider configuration

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Configs/FhirSdkProvider.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Configs/CoreFeatureConfiguration.cs:32-37`
- Create: `src/Microsoft.Health.Fhir.Core.UnitTests/Config/FhirSdkProviderConfigurationTests.cs`

- [ ] **Step 1: Write the failing configuration tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    public class FhirSdkProviderConfigurationTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenProviderRead_ThenFirelyIsSelected()
        {
            var configuration = new CoreFeatureConfiguration();

            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider);
        }

        [Fact]
        public void GivenIgnixaConfigured_WhenProviderRead_ThenIgnixaIsSelected()
        {
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = FhirSdkProvider.Ignixa,
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider);
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify the missing-type failure**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~FhirSdkProviderConfigurationTests --no-restore
```

Expected: build fails because `FhirSdkProvider` and `CoreFeatureConfiguration.FhirSdkProvider` do not exist.

- [ ] **Step 3: Add the two-state enum**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Identifies the preferred FHIR SDK at feature seams that support provider selection.
    /// </summary>
    public enum FhirSdkProvider
    {
        /// <summary>
        /// Use the Firely SDK implementation.
        /// </summary>
        Firely = 0,

        /// <summary>
        /// Use the Ignixa SDK implementation.
        /// </summary>
        Ignixa = 1,
    }
}
```

- [ ] **Step 4: Add the defaulted Core feature property**

Insert after `IncludeTotalInBundle` in `CoreFeatureConfiguration.cs`:

```csharp
        /// <summary>
        /// Gets or sets the preferred FHIR SDK at feature seams that support provider selection.
        /// Firely remains the default until the final migration cutover.
        /// </summary>
        public FhirSdkProvider FhirSdkProvider { get; set; } = FhirSdkProvider.Firely;
```

- [ ] **Step 5: Run the configuration tests on both TFMs**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -f net8.0 --filter FullyQualifiedName~FhirSdkProviderConfigurationTests
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~FhirSdkProviderConfigurationTests
```

Expected: two tests pass on each TFM.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core\Configs\FhirSdkProvider.cs src\Microsoft.Health.Fhir.Core\Configs\CoreFeatureConfiguration.cs src\Microsoft.Health.Fhir.Core.UnitTests\Config\FhirSdkProviderConfigurationTests.cs
git commit -m "Add FHIR SDK provider configuration" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 2: Extract the current parser into version-specific Firely projects

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Operations/Import/ImportResourceIdValidator.cs`
- Create: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Operations/Import/ImportResourceIdValidatorTests.cs`
- Create: `src/Microsoft.Health.Fhir.FirelySdk/Features/Operations/Import/FirelyImportResourceParser.cs`
- Create: four `Microsoft.Health.Fhir.*.FirelySdk.csproj` files listed in the file map.
- Delete: `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Import/ImportResourceParser.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Microsoft.Health.Fhir.Shared.Core.projitems:34`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/OperationsModule.cs:54-57`
- Modify: four version API project files.
- Modify: four version Core unit-test project files.
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/ImportResourceParserTests.cs`

- [ ] **Step 1: Write failing tests for the provider-neutral ID policy**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class ImportResourceIdValidatorTests
    {
        [Theory]
        [InlineData("abc")]
        [InlineData("A1-b.c")]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
        public void GivenValidId_WhenValidated_ThenNoExceptionIsThrown(string id)
        {
            ImportResourceIdValidator.Validate(id);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("contains/slash")]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
        public void GivenInvalidId_WhenValidated_ThenBadRequestIsThrown(string id)
        {
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(id));
        }
    }
}
```

- [ ] **Step 2: Run the policy tests and verify the missing-type failure**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~ImportResourceIdValidatorTests
```

Expected: build fails because `ImportResourceIdValidator` does not exist.

- [ ] **Step 3: Implement the provider-neutral ID policy**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Resources;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Validates resource IDs accepted by the import pipeline.
    /// </summary>
    public static class ImportResourceIdValidator
    {
        private static readonly Regex ResourceIdValidationRegex = new(
            "^[A-Za-z0-9\\-\\.]{1,64}$",
            RegexOptions.Compiled);

        /// <summary>
        /// Validates a resource ID and throws when it is not accepted by the import pipeline.
        /// </summary>
        /// <param name="resourceId">The resource ID to validate.</param>
        public static void Validate(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId) || !ResourceIdValidationRegex.IsMatch(resourceId))
            {
                throw new BadRequestException(
                    $"Invalid resource id: '{resourceId ?? "null or empty"}'. " +
                    Microsoft.Health.Fhir.Core.Resources.IdRequirements);
            }
        }
    }
}
```

- [ ] **Step 4: Run the policy tests**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~ImportResourceIdValidatorTests
```

Expected: all ID policy tests pass.

- [ ] **Step 5: Create the shared Firely parser source by moving current behavior unchanged**

Use `git mv` so history remains visible:

```powershell
New-Item -ItemType Directory -Force .\src\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import | Out-Null
git mv .\src\Microsoft.Health.Fhir.Shared.Core\Features\Operations\Import\ImportResourceParser.cs .\src\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import\FirelyImportResourceParser.cs
```

Rename only the class and constructor; keep the method bodies unchanged:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import
{
    /// <summary>
    /// Parses import resources with the Firely SDK.
    /// </summary>
    public class FirelyImportResourceParser : IImportResourceParser
    {
        private readonly FhirJsonParser _parser;
        private readonly IResourceWrapperFactory _resourceFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyImportResourceParser"/> class.
        /// </summary>
        /// <param name="parser">The Firely JSON parser.</param>
        /// <param name="resourceFactory">The existing Core resource-wrapper factory.</param>
        public FirelyImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory)
        {
            _parser = EnsureArg.IsNotNull(parser, nameof(parser));
            _resourceFactory = EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
        }

        /// <inheritdoc />
        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            var resource = _parser.Parse<Resource>(rawResource);
            ImportResourceIdValidator.Validate(resource?.Id);
            CheckConditionalReferenceInResource(resource, importMode);

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resource.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resource.Meta.LastUpdated.Value;
            resource.Meta.LastUpdated = new DateTimeOffset(lastUpdated.DateTime.TruncateToMillisecond(), lastUpdated.Offset);
            if (!lastUpdatedIsNull && resource.Meta.LastUpdated.Value > Clock.UtcNow.AddSeconds(10))
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resource.Meta.VersionId) || !int.TryParse(resource.Meta.VersionId, out _))
            {
                resource.Meta.VersionId = "1";
                keepVersion = false;
            }

            var resourceElement = resource.ToResourceElement();
            var isDeleted = resourceElement.IsSoftDeleted();

            if (isDeleted)
            {
                resource.Meta.RemoveExtension(KnownFhirPaths.AzureSoftDeletedExtensionUrl);
            }

            var resourceWrapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);
            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWrapper);
        }

        private static void CheckConditionalReferenceInResource(Resource resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad)
            {
                return;
            }

            IEnumerable<ResourceReference> references = resource.GetAllChildren<ResourceReference>();
            foreach (ResourceReference reference in references)
            {
                if (!string.IsNullOrWhiteSpace(reference.Reference) &&
                    reference.Reference.Contains('?', StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
                }
            }
        }

    }
}
```

- [ ] **Step 6: Remove the old shared-source include**

Delete this line from `Microsoft.Health.Fhir.Shared.Core.projitems`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Features\Operations\Import\ImportResourceParser.cs" />
```

- [ ] **Step 7: Create the four provider project files**

Use these exact project shapes:

```xml
<!-- src/Microsoft.Health.Fhir.Stu3.FirelySdk/Microsoft.Health.Fhir.Stu3.FirelySdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Microsoft.Health.Fhir.FirelySdk</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import\FirelyImportResourceParser.cs" Link="Features\Operations\Import\FirelyImportResourceParser.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Health.Fhir.Stu3.Core\Microsoft.Health.Fhir.Stu3.Core.csproj" />
  </ItemGroup>
</Project>

<!-- src/Microsoft.Health.Fhir.R4.FirelySdk/Microsoft.Health.Fhir.R4.FirelySdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Microsoft.Health.Fhir.FirelySdk</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import\FirelyImportResourceParser.cs" Link="Features\Operations\Import\FirelyImportResourceParser.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Health.Fhir.R4.Core\Microsoft.Health.Fhir.R4.Core.csproj" />
  </ItemGroup>
</Project>

<!-- src/Microsoft.Health.Fhir.R4B.FirelySdk/Microsoft.Health.Fhir.R4B.FirelySdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Microsoft.Health.Fhir.FirelySdk</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import\FirelyImportResourceParser.cs" Link="Features\Operations\Import\FirelyImportResourceParser.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Health.Fhir.R4B.Core\Microsoft.Health.Fhir.R4B.Core.csproj" />
  </ItemGroup>
</Project>

<!-- src/Microsoft.Health.Fhir.R5.FirelySdk/Microsoft.Health.Fhir.R5.FirelySdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Microsoft.Health.Fhir.FirelySdk</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\Microsoft.Health.Fhir.FirelySdk\Features\Operations\Import\FirelyImportResourceParser.cs" Link="Features\Operations\Import\FirelyImportResourceParser.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Health.Fhir.R5.Core\Microsoft.Health.Fhir.R5.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Add provider references to each version API and Core unit-test project**

Add the matching Firely project reference to:

```xml
<ProjectReference Include="..\Microsoft.Health.Fhir.Stu3.FirelySdk\Microsoft.Health.Fhir.Stu3.FirelySdk.csproj" />
<ProjectReference Include="..\Microsoft.Health.Fhir.R4.FirelySdk\Microsoft.Health.Fhir.R4.FirelySdk.csproj" />
<ProjectReference Include="..\Microsoft.Health.Fhir.R4B.FirelySdk\Microsoft.Health.Fhir.R4B.FirelySdk.csproj" />
<ProjectReference Include="..\Microsoft.Health.Fhir.R5.FirelySdk\Microsoft.Health.Fhir.R5.FirelySdk.csproj" />
```

Each project receives only its matching reference. Apply this to:

- `src/Microsoft.Health.Fhir.Stu3.Api/Microsoft.Health.Fhir.Stu3.Api.csproj`
- `src/Microsoft.Health.Fhir.R4.Api/Microsoft.Health.Fhir.R4.Api.csproj`
- `src/Microsoft.Health.Fhir.R4B.Api/Microsoft.Health.Fhir.R4B.Api.csproj`
- `src/Microsoft.Health.Fhir.R5.Api/Microsoft.Health.Fhir.R5.Api.csproj`
- `src/Microsoft.Health.Fhir.Stu3.Core.UnitTests/Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj`
- `src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj`
- `src/Microsoft.Health.Fhir.R4B.Core.UnitTests/Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj`
- `src/Microsoft.Health.Fhir.R5.Core.UnitTests/Microsoft.Health.Fhir.R5.Core.UnitTests.csproj`

- [ ] **Step 9: Point the existing parser tests at the extracted Firely class**

Replace:

```csharp
private readonly ImportResourceParser _importResourceParser;
```

with:

```csharp
private readonly FirelyImportResourceParser _importResourceParser;
```

Add:

```csharp
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
```

Keep the test behavior and constructor arguments unchanged.

- [ ] **Step 10: Preserve unconditional Firely registration**

Replace the old registration in `OperationsModule.cs`:

```csharp
services.Add<FirelyImportResourceParser>()
    .Transient()
    .AsService<IImportResourceParser>();
```

Add:

```csharp
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
```

This keeps the repository buildable after the old shared parser is removed and does not introduce provider selection yet.

- [ ] **Step 11: Build all Firely provider projects on both TFMs**

Run:

```powershell
$projects = @(
  ".\src\Microsoft.Health.Fhir.Stu3.FirelySdk\Microsoft.Health.Fhir.Stu3.FirelySdk.csproj",
  ".\src\Microsoft.Health.Fhir.R4.FirelySdk\Microsoft.Health.Fhir.R4.FirelySdk.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.FirelySdk\Microsoft.Health.Fhir.R4B.FirelySdk.csproj",
  ".\src\Microsoft.Health.Fhir.R5.FirelySdk\Microsoft.Health.Fhir.R5.FirelySdk.csproj"
)
foreach ($project in $projects) {
  dotnet build $project -f net8.0 --no-restore
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  dotnet build $project -f net9.0 --no-restore
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: eight successful builds.

- [ ] **Step 12: Run the existing import parser tests for all versions**

```powershell
$tests = @(
  ".\src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj"
)
foreach ($test in $tests) {
  dotnet test $test -f net9.0 --filter FullyQualifiedName~ImportResourceParserTests --no-restore
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: the existing parser tests pass unchanged for each version.

- [ ] **Step 13: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core\Features\Operations\Import\ImportResourceIdValidator.cs src\Microsoft.Health.Fhir.Core.UnitTests\Features\Operations\Import\ImportResourceIdValidatorTests.cs src\Microsoft.Health.Fhir.FirelySdk src\Microsoft.Health.Fhir.*.FirelySdk src\Microsoft.Health.Fhir.Shared.Api\Modules\OperationsModule.cs src\Microsoft.Health.Fhir.Shared.Core src\Microsoft.Health.Fhir.Shared.Core.UnitTests src\Microsoft.Health.Fhir.*.Api\*.csproj src\Microsoft.Health.Fhir.*.Core.UnitTests\*.csproj
git commit -m "Extract Firely import providers" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 3: Add the minimal net9 Ignixa provider project and schema selection

**Files:**
- Modify: `Directory.Packages.props`
- Create: `src/Microsoft.Health.Fhir.Ignixa/Microsoft.Health.Fhir.Ignixa.csproj`
- Create: `src/Microsoft.Health.Fhir.Ignixa/IgnixaSchemaContext.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/IgnixaSchemaContextTests.cs`
- Modify: four version Core unit-test project files.
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems`

- [ ] **Step 1: Pin only the Ignixa packages required by Phase 0**

Add to the shared property group:

```xml
<IgnixaPackageVersion>0.0.163</IgnixaPackageVersion>
```

Add to the package-version item group:

```xml
<PackageVersion Include="Ignixa.Abstractions" Version="$(IgnixaPackageVersion)" />
<PackageVersion Include="Ignixa.Extensions.FirelySdk5" Version="$(IgnixaPackageVersion)" />
<PackageVersion Include="Ignixa.Serialization" Version="$(IgnixaPackageVersion)" />
<PackageVersion Include="Ignixa.Specification" Version="$(IgnixaPackageVersion)" />
```

Do not add Ignixa validation, FHIRPath, search, package-management, or ASP.NET formatter packages.

- [ ] **Step 2: Create the net9-only Ignixa project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0</TargetFrameworks>
    <RootNamespace>Microsoft.Health.Fhir.Ignixa</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Health.Fhir.Core\Microsoft.Health.Fhir.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Ignixa.Abstractions" />
    <PackageReference Include="Ignixa.Extensions.FirelySdk5" />
    <PackageReference Include="Ignixa.Serialization" />
    <PackageReference Include="Ignixa.Specification" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add conditional Ignixa references to all version Core unit-test projects**

Add this reference to each version Core unit-test project:

```xml
<ProjectReference Include="..\Microsoft.Health.Fhir.Ignixa\Microsoft.Health.Fhir.Ignixa.csproj" Condition="'$(TargetFramework)' == 'net9.0'" />
```

- [ ] **Step 4: Write the failing generated-schema test**

Create `IgnixaSchemaContextTests.cs` and add it to the shared unit-test `.projitems`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#if NET9_0_OR_GREATER
using Ignixa.Specification.Generated;
using Microsoft.Health.Fhir.Ignixa;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    public class IgnixaSchemaContextTests
    {
        [Fact]
        public void GivenCurrentFhirVersion_WhenContextCreated_ThenMatchingGeneratedSchemaIsSelected()
        {
            var context = new IgnixaSchemaContext(new VersionSpecificModelInfoProvider());

#if Stu3
            Assert.IsType<STU3CoreSchemaProvider>(context.Schema);
#elif R4
            Assert.IsType<R4CoreSchemaProvider>(context.Schema);
#elif R4B
            Assert.IsType<R4BCoreSchemaProvider>(context.Schema);
#elif R5
            Assert.IsType<R5CoreSchemaProvider>(context.Schema);
#else
#error Unsupported FHIR version
#endif
        }
    }
}
#endif
```

Add this include after the existing import test includes:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Features\Operations\Import\IgnixaSchemaContextTests.cs" />
```

- [ ] **Step 5: Run the schema test and verify it fails**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~IgnixaSchemaContextTests
```

Expected: build fails because `IgnixaSchemaContext` does not exist.

- [ ] **Step 6: Implement schema selection**

> **Revised during review:** `Schema` is typed as `IFhirSchemaProvider` (not the narrower `ISchema`) below —
> both are implemented by the same generated provider classes, so this is a safe widening — so that the
> parser can reach `Schema.ReferenceMetadataProvider` for schema-driven conditional-reference checks (see
> Task 4, Step 3) without any additional package.

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Provides the generated Ignixa schema for the server's FHIR version.
    /// </summary>
    public sealed class IgnixaSchemaContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaSchemaContext"/> class.
        /// </summary>
        /// <param name="modelInfoProvider">The server's FHIR model information.</param>
        public IgnixaSchemaContext(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            Schema = CreateSchema(modelInfoProvider.Version);
        }

        /// <summary>
        /// Gets the generated Ignixa schema for the server's FHIR version, including reference metadata.
        /// </summary>
        public IFhirSchemaProvider Schema { get; }

        private static IFhirSchemaProvider CreateSchema(FhirSpecification version)
        {
            return version switch
            {
                FhirSpecification.Stu3 => new STU3CoreSchemaProvider(),
                FhirSpecification.R4 => new R4CoreSchemaProvider(),
                FhirSpecification.R4B => new R4BCoreSchemaProvider(),
                FhirSpecification.R5 => new R5CoreSchemaProvider(),
                _ => throw new NotSupportedException($"FHIR version {version} is not supported by Ignixa import."),
            };
        }
    }
}
```

- [ ] **Step 7: Run schema tests for all four versions**

```powershell
$tests = @(
  ".\src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj"
)
foreach ($test in $tests) {
  dotnet test $test -f net9.0 --filter FullyQualifiedName~IgnixaSchemaContextTests
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: each version reports the matching generated schema provider.

- [ ] **Step 8: Verify net8 restore does not include the Ignixa project**

```powershell
dotnet restore .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj -p:TargetFramework=net8.0
dotnet build .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj -f net8.0 --no-restore
```

Expected: restore and build succeed without resolving Ignixa assemblies for net8.

- [ ] **Step 9: Commit**

```powershell
git add Directory.Packages.props src\Microsoft.Health.Fhir.Ignixa src\Microsoft.Health.Fhir.*.Core.UnitTests src\Microsoft.Health.Fhir.Shared.Core.UnitTests
git commit -m "Add multi-version Ignixa import provider" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 4: Implement Ignixa import behavior test-first

**Files:**
- Create: `src/Microsoft.Health.Fhir.Ignixa/Features/Operations/Import/IgnixaImportResourceParser.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/ImportResourceParserParityTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems`

- [ ] **Step 1: Add the shared parser fixture and first parity test**

Create `ImportResourceParserParityTests.cs` with this initial content:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#if NET9_0_OR_GREATER
using System;
using System.Text.Json.Nodes;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
public class ImportResourceParserParityTests
{
private readonly IImportResourceParser _firelyParser;
private readonly IImportResourceParser _ignixaParser;

public ImportResourceParserParityTests()
{
    var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
    requestContextAccessor.RequestContext.Method.Returns("PUT");
    requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

    var wrapperFactory = new ResourceWrapperFactory(
        new RawResourceFactory(new FhirJsonSerializer()),
        requestContextAccessor,
        Substitute.For<ISearchIndexer>(),
        Substitute.For<IClaimsExtractor>(),
        Substitute.For<ICompartmentIndexer>(),
        Substitute.For<ISearchParameterDefinitionManager>(),
        Deserializers.ResourceDeserializer);

    _firelyParser = new FirelyImportResourceParser(new FhirJsonParser(), wrapperFactory);
    _ignixaParser = new IgnixaImportResourceParser(
        wrapperFactory,
        new IgnixaSchemaContext(new VersionSpecificModelInfoProvider()));
}

[Fact]
public void GivenValidResource_WhenParsed_ThenProvidersProduceEquivalentImportMetadata()
{
    const string json = """
        {
          "resourceType": "Patient",
          "id": "patient-1",
          "meta": {
            "versionId": "7",
            "lastUpdated": "2026-01-02T03:04:05.123Z"
          },
          "active": true
        }
        """;

    ImportResource firely = _firelyParser.Parse(4, 10, json.Length, json, ImportMode.IncrementalLoad);
    ImportResource ignixa = _ignixaParser.Parse(4, 10, json.Length, json, ImportMode.IncrementalLoad);

    Assert.Equal(firely.Index, ignixa.Index);
    Assert.Equal(firely.Offset, ignixa.Offset);
    Assert.Equal(firely.Length, ignixa.Length);
    Assert.Equal(firely.KeepLastUpdated, ignixa.KeepLastUpdated);
    Assert.Equal(firely.KeepVersion, ignixa.KeepVersion);
    Assert.Equal(firely.IsDeleted, ignixa.IsDeleted);
    Assert.Equal(firely.ResourceWrapper.ResourceId, ignixa.ResourceWrapper.ResourceId);
    Assert.Equal(firely.ResourceWrapper.Version, ignixa.ResourceWrapper.Version);
    Assert.True(
        JsonNode.DeepEquals(
            JsonNode.Parse(firely.ResourceWrapper.RawResource.Data),
            JsonNode.Parse(ignixa.ResourceWrapper.RawResource.Data)));
}
}
}
#endif
```

Add this line to the shared unit-test `.projitems`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Features\Operations\Import\ImportResourceParserParityTests.cs" />
```

- [ ] **Step 2: Run the test and verify the missing-parser failure**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~ImportResourceParserParityTests
```

Expected: build fails because `IgnixaImportResourceParser` does not exist.

- [ ] **Step 3: Implement the Ignixa parser**

Use the following class. It deliberately converts to the public Firely-shaped `ResourceElement` before calling `IResourceWrapperFactory`; do not preserve the native node in Phase 0.

> **Revised during review:** the version below supersedes an earlier draft that detected soft-delete via a
> hardcoded list of `value[x]` property names, and conditional references via a blind recursive walk of the
> entire raw JSON graph. Both were replaced:
> - Soft-delete now uses the same FHIRPath predicate the Firely parser uses (`ResourceElement.IsSoftDeleted()`)
>   instead of hand-rolling the `value='soft-deleted'` comparison — single source of truth, no risk of drifting
>   from Firely on a future FHIR version or primitive type.
> - Conditional-reference checking now uses `IFhirSchemaProvider.ReferenceMetadataProvider` (already available
>   via the `Ignixa.Specification` package this plan already pins — no new package) to check only the
>   schema-declared reference fields for the resource's own type, instead of walking the whole JSON graph. This
>   is both faster (bounded by the resource's own reference-field count, not its total size) and intentionally
>   scoped: it does not recurse into `contained` resources or Bundle entries. `TypedElementSearchIndexer`
>   likewise never indexes into `contained`, and `Bundle` has no reference metadata of its own (entries are
>   nested resources, not Reference-typed fields), so both are legitimately out of scope for $import.
> - A load-bearing gotcha: `ResourceJsonNode` caches its converted `IElement` per instance
>   (`_cachedElement`/`_cachedProvider` fields). Mutating `MutableNode` and calling `ToElement()` again on the
>   *same* node silently returns the stale pre-mutation element. When soft-deleted, re-parse a fresh
>   `ResourceJsonNode` from the mutated JSON before the final `ToElement()` call.

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa.Features.Operations.Import
{
    /// <summary>
    /// Ignixa based implementation of <see cref="IImportResourceParser"/> that parses raw NDJSON
    /// resource content into <see cref="ImportResource"/> instances for the $import operation.
    /// </summary>
    public sealed class IgnixaImportResourceParser : IImportResourceParser
    {
        private readonly IResourceWrapperFactory _resourceFactory;
        private readonly IgnixaSchemaContext _schemaContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaImportResourceParser"/> class.
        /// </summary>
        /// <param name="resourceFactory">The factory used to create resource wrappers.</param>
        /// <param name="schemaContext">The Ignixa generated schema for the current FHIR version.</param>
        public IgnixaImportResourceParser(IResourceWrapperFactory resourceFactory, IgnixaSchemaContext schemaContext)
        {
            EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
            EnsureArg.IsNotNull(schemaContext, nameof(schemaContext));

            _resourceFactory = resourceFactory;
            _schemaContext = schemaContext;
        }

        /// <inheritdoc />
        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            ResourceJsonNode resource;
            try
            {
                resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(rawResource);
            }
            catch (JsonException exception)
            {
                throw new FormatException("Failed to parse import resource JSON.", exception);
            }

            ImportResourceIdValidator.Validate(resource.Id);
            CheckConditionalReferenceInResource(resource, importMode);

            resource.Meta ??= new MetaJsonNode();

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resource.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resource.Meta.LastUpdated.Value;
            resource.Meta.LastUpdated = new DateTimeOffset(lastUpdated.DateTime.TruncateToMillisecond(), lastUpdated.Offset);
            if (!lastUpdatedIsNull && resource.Meta.LastUpdated.Value > Clock.UtcNow.AddSeconds(10)) // 10 sec is the max for the computers in the domain
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resource.Meta.VersionId) || !int.TryParse(resource.Meta.VersionId, out var _))
            {
                resource.Meta.VersionId = "1";
                keepVersion = false;
            }

            var resourceElement = new ResourceElement(resource.ToElement(_schemaContext.Schema).ToTypedElement());
            var isDeleted = resourceElement.IsSoftDeleted();

            if (isDeleted)
            {
                // ResourceJsonNode caches its converted IElement internally (per node instance), so mutating
                // resource.MutableNode and calling ToElement() again on the *same* node would silently return
                // the stale pre-mutation element. Re-parse a fresh node from the mutated JSON instead.
                RemoveSoftDeletedExtension(resource.MutableNode);
                resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(resource.MutableNode.ToJsonString());
                resourceElement = new ResourceElement(resource.ToElement(_schemaContext.Schema).ToTypedElement());
            }

            var resourceWrapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWrapper);
        }

        /// <summary>
        /// Rejects conditional references (a "reference" value containing '?') found in the fields the
        /// generated Ignixa schema declares as Reference-typed for this resource's own type.
        /// </summary>
        /// <remarks>
        /// Scoped to the resource's direct, schema-declared reference fields only — matching
        /// <see cref="Microsoft.Health.Fhir.Core.Features.Search.TypedElementSearchIndexer"/>, which likewise
        /// never indexes into <c>contained</c> resources. Bundle entries are out of scope for $import: the
        /// generated schema has no reference metadata for <c>Bundle</c> itself (each entry is a distinct,
        /// independently-typed resource, not a Reference-typed field of Bundle), and import NDJSON is expected
        /// to contain individual resources rather than transactional Bundles.
        /// </remarks>
        private void CheckConditionalReferenceInResource(ResourceJsonNode resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad || resource.MutableNode is not JsonObject root)
            {
                return;
            }

            foreach (ReferenceFieldMetadata field in _schemaContext.Schema.ReferenceMetadataProvider.GetMetadata(resource.ResourceType))
            {
                var propertyName = field.ElementPath.EndsWith("[x]", StringComparison.Ordinal)
                    ? string.Concat(field.ElementPath.AsSpan(0, field.ElementPath.Length - 3), "Reference")
                    : field.ElementPath;

                if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
                {
                    continue;
                }

                if (field.IsCollection)
                {
                    if (value is JsonArray array)
                    {
                        foreach (var item in array)
                        {
                            ThrowIfConditionalReference(item);
                        }
                    }
                }
                else
                {
                    ThrowIfConditionalReference(value);
                }
            }
        }

        private static void ThrowIfConditionalReference(JsonNode referenceNode)
        {
            if (referenceNode is JsonObject referenceObject &&
                referenceObject.TryGetPropertyValue("reference", out var referenceValue) &&
                referenceValue is JsonValue jsonValue &&
                jsonValue.TryGetValue(out string reference) &&
                reference.Contains('?', StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
            }
        }

        /// <summary>
        /// Removes every extension whose URL matches <see cref="KnownFhirPaths.AzureSoftDeletedExtensionUrl"/>,
        /// removing the now-empty extension array if applicable. Only called once <see cref="ResourceElement"/>'s
        /// FHIRPath-driven <c>IsSoftDeleted()</c> predicate (the same one the Firely parser uses) has already
        /// confirmed the resource is soft-deleted, so — mirroring Firely's <c>Meta.RemoveExtension</c> — every
        /// extension matching the URL is removed regardless of its value.
        /// </summary>
        /// <param name="root">The raw JSON graph for the resource.</param>
        private static void RemoveSoftDeletedExtension(JsonNode root)
        {
            if (root?["meta"] is not JsonObject meta || meta["extension"] is not JsonArray extensions)
            {
                return;
            }

            for (var i = extensions.Count - 1; i >= 0; i--)
            {
                if (extensions[i] is JsonObject extension &&
                    extension["url"]?.GetValue<string>() is string url &&
                    string.Equals(url, KnownFhirPaths.AzureSoftDeletedExtensionUrl, StringComparison.Ordinal))
                {
                    extensions.RemoveAt(i);
                }
            }

            if (extensions.Count == 0)
            {
                meta.Remove("extension");
            }
        }
    }
}
```

- [ ] **Step 4: Run the first parity test**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~GivenValidResource_WhenParsed_ThenProvidersProduceEquivalentImportMetadata
```

Expected: one passing test.

- [ ] **Step 5: Add parity cases for metadata normalization and invalid IDs**

Add tests that invoke both parsers and compare:

```csharp
[Theory]
[InlineData("valid-id", false)]
[InlineData("a.b-c", false)]
[InlineData("", true)]
[InlineData("contains/slash", true)]
[InlineData("01234567890123456789012345678901234567890123456789012345678901234", true)]
public void GivenResourceId_WhenParsed_ThenProvidersAgree(string id, bool shouldThrow)
{
    string json = $$"""{"resourceType":"Patient","id":"{{id}}"}""";

    Exception firely = Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
    Exception ignixa = Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

    Assert.Equal(shouldThrow, firely != null);
    Assert.Equal(shouldThrow, ignixa != null);
    Assert.Equal(firely?.GetType(), ignixa?.GetType());
}

[Fact]
public void GivenInitialLoad_WhenParsed_ThenProvidersResetVersionAndLastUpdated()
{
    const string json = """
        {
          "resourceType":"Patient",
          "id":"patient-1",
          "meta":{"versionId":"9","lastUpdated":"2020-01-01T00:00:00Z"}
        }
        """;

    ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad);
    ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad);

    Assert.False(firely.KeepVersion);
    Assert.False(ignixa.KeepVersion);
    Assert.Equal("1", firely.ResourceWrapper.Version);
    Assert.Equal("1", ignixa.ResourceWrapper.Version);
    Assert.NotEqual(default, firely.ResourceWrapper.LastModified);
    Assert.NotEqual(default, ignixa.ResourceWrapper.LastModified);
}
```

- [ ] **Step 6: Add recursive conditional-reference tests**

```csharp
[Theory]
[InlineData(ImportMode.InitialLoad, true)]
[InlineData(ImportMode.IncrementalLoad, false)]
public void GivenContainedConditionalReference_WhenParsed_ThenProvidersAgree(
    ImportMode importMode,
    bool shouldThrow)
{
    const string json = """
        {
          "resourceType":"Patient",
          "id":"patient-1",
          "contained":[{
            "resourceType":"Observation",
            "id":"obs-1",
            "subject":{"reference":"Patient?identifier=system|value"},
            "status":"final",
            "code":{"text":"test"}
          }]
        }
        """;

    Exception firely = Record.Exception(() => _firelyParser.Parse(0, 0, json.Length, json, importMode));
    Exception ignixa = Record.Exception(() => _ignixaParser.Parse(0, 0, json.Length, json, importMode));

    Assert.Equal(shouldThrow, firely != null);
    Assert.Equal(shouldThrow, ignixa != null);
    Assert.Equal(firely?.GetType(), ignixa?.GetType());
}

[Fact]
public void GivenBundleEntryConditionalReferenceDuringInitialLoad_WhenParsed_ThenBothProvidersReject()
{
    const string json = """
        {
          "resourceType":"Bundle",
          "id":"bundle-1",
          "type":"collection",
          "entry":[{
            "resource":{
              "resourceType":"Observation",
              "id":"obs-1",
              "subject":{"reference":"Patient?identifier=system|value"},
              "status":"final",
              "code":{"text":"test"}
            }
          }]
        }
        """;

    Assert.Throws<NotSupportedException>(
        () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
    Assert.Throws<NotSupportedException>(
        () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));
}
```

- [ ] **Step 7: Add soft-delete and malformed-input tests**

```csharp
[Fact]
public void GivenSoftDeletedResource_WhenParsed_ThenBothProvidersRemoveExtensionAndMarkDeleted()
{
    string json = $$"""
        {
          "resourceType":"Patient",
          "id":"patient-1",
          "meta":{
            "extension":[{
              "url":"{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}",
              "valueString":"soft-deleted"
            }]
          }
        }
        """;

    ImportResource firely = _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
    ImportResource ignixa = _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

    Assert.True(firely.IsDeleted);
    Assert.True(ignixa.IsDeleted);
    Assert.DoesNotContain(
        KnownFhirPaths.AzureSoftDeletedExtensionUrl,
        firely.ResourceWrapper.RawResource.Data,
        StringComparison.Ordinal);
    Assert.DoesNotContain(
        KnownFhirPaths.AzureSoftDeletedExtensionUrl,
        ignixa.ResourceWrapper.RawResource.Data,
        StringComparison.Ordinal);
}

[Theory]
[InlineData("{")]
[InlineData("""{"resourceType":"NoSuchResource","id":"x"}""")]
public void GivenInvalidResourceJson_WhenParsed_ThenNeitherProviderReturnsSuccess(string json)
{
    Exception firely = Record.Exception(
        () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
    Exception ignixa = Record.Exception(
        () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));

    Assert.NotNull(firely);
    Assert.NotNull(ignixa);
}

[Fact]
public void GivenFutureLastUpdated_WhenParsed_ThenBothProvidersReject()
{
    const string json = """
        {
          "resourceType":"Patient",
          "id":"patient-1",
          "meta":{"lastUpdated":"2999-01-01T00:00:00Z"}
        }
        """;

    Assert.Throws<NotSupportedException>(
        () => _firelyParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
    Assert.Throws<NotSupportedException>(
        () => _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad));
}
```

- [ ] **Step 8: Run the full parity class for all versions**

```powershell
$tests = @(
  ".\src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj"
)
foreach ($test in $tests) {
  dotnet test $test -f net9.0 --filter FullyQualifiedName~ImportResourceParserParityTests
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: all parity cases pass for STU3, R4, R4B, and R5.

- [ ] **Step 9: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Ignixa\Features\Operations\Import src\Microsoft.Health.Fhir.Shared.Core.UnitTests
git commit -m "Implement Ignixa import parsing" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 5: Select the import provider in the composition root

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/OperationsModule.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirSdkProviderStartupLogger.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems`
- Create: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/OperationsModuleTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems`
- Modify: four version API project files.

- [ ] **Step 1: Add conditional Ignixa project references to each version API project**

Add to all four version API project files:

```xml
<ProjectReference Include="..\Microsoft.Health.Fhir.Ignixa\Microsoft.Health.Fhir.Ignixa.csproj" Condition="'$(TargetFramework)' == 'net9.0'" />
```

The matching Firely references were added in Task 2.

- [ ] **Step 2: Write registration tests**

Add `OperationsModuleTests.cs` to the shared API unit-test `.projitems` and use the following tests:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    public class OperationsModuleTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenModuleLoads_ThenFirelyParserIsRegistered()
        {
            var configuration = new FhirServerConfiguration();
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services.Where(x => x.ServiceType == typeof(IImportResourceParser)));
            Assert.Equal("FirelyImportResourceParser", descriptor.ImplementationType.Name);
            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IHostedService) &&
                    x.ImplementationType == typeof(FhirSdkProviderStartupLogger));
        }

#if NET9_0_OR_GREATER
        [Fact]
        public void GivenIgnixaConfiguration_WhenModuleLoads_ThenIgnixaParserIsRegistered()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider = FhirSdkProvider.Ignixa;
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services.Where(x => x.ServiceType == typeof(IImportResourceParser)));
            Assert.Equal("IgnixaImportResourceParser", descriptor.ImplementationType.Name);
        }
#else
        [Fact]
        public void GivenIgnixaConfigurationOnNet8_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider = FhirSdkProvider.Ignixa;

            NotSupportedException exception = Assert.Throws<NotSupportedException>(
                () => new OperationsModule(configuration).Load(new ServiceCollection()));

            Assert.Contains("net9.0", exception.Message, StringComparison.Ordinal);
        }
#endif

        [Fact]
        public void GivenUnknownProvider_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider = (FhirSdkProvider)999;

            Assert.Throws<InvalidOperationException>(
                () => new OperationsModule(configuration).Load(new ServiceCollection()));
        }
    }
}
```

Add to `Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Modules\OperationsModuleTests.cs" />
```

- [ ] **Step 3: Run the tests and verify selection is not implemented**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~OperationsModuleTests
```

Expected: build or assertion failure because `OperationsModule` has no configuration constructor and still registers the old parser type.

- [ ] **Step 4: Implement one composition-root decision**

Add a constructor that reads `FhirServerConfiguration.CoreFeatures.FhirSdkProvider`. Replace only the current parser registration block:

```csharp
private readonly FhirSdkProvider _fhirSdkProvider;

public OperationsModule(FhirServerConfiguration fhirServerConfiguration)
{
    EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
    _fhirSdkProvider = fhirServerConfiguration.CoreFeatures.FhirSdkProvider;
}
```

```csharp
switch (_fhirSdkProvider)
{
    case FhirSdkProvider.Firely:
        services.Add<FirelyImportResourceParser>()
            .Transient()
            .AsService<IImportResourceParser>();
        break;

    case FhirSdkProvider.Ignixa:
#if NET9_0_OR_GREATER
        services.Add<IgnixaSchemaContext>()
            .Singleton()
            .AsSelf();

        services.Add<IgnixaImportResourceParser>()
            .Transient()
            .AsService<IImportResourceParser>();
        break;
#else
        throw new NotSupportedException(
            "FhirSdkProvider.Ignixa requires net9.0. Configure Firely when running net8.0.");
#endif

    default:
        throw new InvalidOperationException($"Unsupported FHIR SDK provider: {_fhirSdkProvider}.");
}
```

Add provider `using` directives, with the Ignixa directives inside `#if NET9_0_OR_GREATER`.

- [ ] **Step 5: Add the startup logger**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Logs the configured FHIR SDK provider and the seams controlled by it.
    /// </summary>
    public sealed class FhirSdkProviderStartupLogger : IHostedService
    {
        private readonly FhirSdkProvider _provider;
        private readonly ILogger<FhirSdkProviderStartupLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirSdkProviderStartupLogger"/> class.
        /// </summary>
        /// <param name="configuration">The Core feature configuration.</param>
        /// <param name="logger">The startup logger.</param>
        public FhirSdkProviderStartupLogger(
            IOptions<CoreFeatureConfiguration> configuration,
            ILogger<FhirSdkProviderStartupLogger> logger)
        {
            _provider = configuration.Value.FhirSdkProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "FHIR SDK provider configured: {FhirSdkProvider}; migrated seams: Import.",
                _provider);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
```

Register it once in `OperationsModule.Load`:

```csharp
services.Add<FhirSdkProviderStartupLogger>()
    .Singleton()
    .AsService<IHostedService>();
```

Add the logger to `Microsoft.Health.Fhir.Shared.Api.projitems` next to `OperationsModule.cs`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Modules\FhirSdkProviderStartupLogger.cs" />
```

- [ ] **Step 6: Run registration tests on net8 and net9**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj -f net8.0 --filter FullyQualifiedName~OperationsModuleTests
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~OperationsModuleTests
```

Expected:

- net8: Firely default, Ignixa startup rejection, and unknown-value tests pass.
- net9: Firely, Ignixa, and unknown-value tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Api src\Microsoft.Health.Fhir.Shared.Api.UnitTests src\Microsoft.Health.Fhir.*.Api\*.csproj
git commit -m "Select import parser by SDK provider" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 6: Add solution, Docker, and explicit default wiring

**Files:**
- Modify: `Microsoft.Health.Fhir.sln`
- Modify: `R4.slnf`
- Modify: `R5.slnf`
- Modify: `build/docker/Dockerfile:53-60`
- Modify: `src/Microsoft.Health.Fhir.Shared.Web/appsettings.json:24-31`

- [ ] **Step 1: Add all provider projects to the solution using the CLI**

```powershell
dotnet sln .\Microsoft.Health.Fhir.sln add `
  .\src\Microsoft.Health.Fhir.Stu3.FirelySdk\Microsoft.Health.Fhir.Stu3.FirelySdk.csproj `
  .\src\Microsoft.Health.Fhir.R4.FirelySdk\Microsoft.Health.Fhir.R4.FirelySdk.csproj `
  .\src\Microsoft.Health.Fhir.R4B.FirelySdk\Microsoft.Health.Fhir.R4B.FirelySdk.csproj `
  .\src\Microsoft.Health.Fhir.R5.FirelySdk\Microsoft.Health.Fhir.R5.FirelySdk.csproj `
  .\src\Microsoft.Health.Fhir.Ignixa\Microsoft.Health.Fhir.Ignixa.csproj
```

- [ ] **Step 2: Add relevant projects to solution filters**

Add to `R4.slnf`:

```json
"src\\Microsoft.Health.Fhir.R4.FirelySdk\\Microsoft.Health.Fhir.R4.FirelySdk.csproj",
"src\\Microsoft.Health.Fhir.Ignixa\\Microsoft.Health.Fhir.Ignixa.csproj",
```

Add to `R5.slnf`:

```json
"src\\Microsoft.Health.Fhir.R5.FirelySdk\\Microsoft.Health.Fhir.R5.FirelySdk.csproj",
"src\\Microsoft.Health.Fhir.Ignixa\\Microsoft.Health.Fhir.Ignixa.csproj",
```

Keep the JSON arrays sorted with the existing project grouping.

- [ ] **Step 3: Add provider project files to the Docker restore layer**

Insert before the version API copy:

```dockerfile
COPY ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.FirelySdk/Microsoft.Health.Fhir.${FHIR_VERSION}.FirelySdk.csproj \
     ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.FirelySdk/Microsoft.Health.Fhir.${FHIR_VERSION}.FirelySdk.csproj

COPY ./src/Microsoft.Health.Fhir.Ignixa/Microsoft.Health.Fhir.Ignixa.csproj \
     ./src/Microsoft.Health.Fhir.Ignixa/Microsoft.Health.Fhir.Ignixa.csproj
```

The Docker publish already targets net9, so no conditional Docker path is needed.

- [ ] **Step 4: Make the deployed default explicit**

Add under `FhirServer.CoreFeatures` in `src/Microsoft.Health.Fhir.Shared.Web/appsettings.json`:

```json
"FhirSdkProvider": "Firely",
```

Do not add Ignixa overrides to production or test deployment templates in this PR.

- [ ] **Step 5: Restore the R4 and R5 filters**

```powershell
dotnet restore .\R4.slnf
dotnet restore .\R5.slnf
```

Expected: both restores succeed on the current SDK without removing net8.

- [ ] **Step 6: Build one Docker image**

```powershell
docker build --build-arg FHIR_VERSION=R4 --build-arg ASSEMBLY_VER=0.0.0-local -f .\build\docker\Dockerfile .
```

Expected: restore and net9 publish complete; the provider projects are found in the restore layer.

- [ ] **Step 7: Commit**

```powershell
git add Microsoft.Health.Fhir.sln R4.slnf R5.slnf build\docker\Dockerfile src\Microsoft.Health.Fhir.Shared.Web\appsettings.json
git commit -m "Wire provider projects into builds" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 89d1a202-b566-47b1-a1e5-1633ba40a498"
```

### Task 7: Verify Phase 0 as one reviewable feature slice

**Files:**
- Modify only if verification reveals a Phase 0 defect.

- [ ] **Step 1: Check scope before running broad validation**

```powershell
git --no-pager diff main...HEAD --stat
git --no-pager diff main...HEAD --name-only
```

Expected:

- No formatter, validation, FHIRPath, persistence, SQL, Cosmos, controller, .NET target, CI, AAD, or Firely package-version changes.
- Existing production C# edits are limited to configuration, import registration, and the moved Firely parser.

- [ ] **Step 2: Run targeted import and registration tests across the version/TFM matrix**

```powershell
$coreTests = @(
  ".\src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj"
)
foreach ($test in $coreTests) {
  dotnet test $test -f net8.0 --filter FullyQualifiedName~ImportResourceParser
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  dotnet test $test -f net9.0 --filter FullyQualifiedName~ImportResourceParser
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected:

- Firely import tests pass on net8 and net9 for all versions.
- Ignixa parity tests compile and run only on net9.

- [ ] **Step 3: Run API registration tests for every version**

```powershell
$apiTests = @(
  ".\src\Microsoft.Health.Fhir.Stu3.Api.UnitTests\Microsoft.Health.Fhir.Stu3.Api.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R4B.Api.UnitTests\Microsoft.Health.Fhir.R4B.Api.UnitTests.csproj",
  ".\src\Microsoft.Health.Fhir.R5.Api.UnitTests\Microsoft.Health.Fhir.R5.Api.UnitTests.csproj"
)
foreach ($test in $apiTests) {
  dotnet test $test -f net8.0 --filter FullyQualifiedName~OperationsModuleTests
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  dotnet test $test -f net9.0 --filter FullyQualifiedName~OperationsModuleTests
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: selection and unsupported-TFM tests pass in all version assemblies.

- [ ] **Step 4: Build R4 and R5 solution filters**

```powershell
dotnet build .\R4.slnf --no-restore
dotnet build .\R5.slnf --no-restore
```

Expected: both filters build with zero errors.

- [ ] **Step 5: Run targeted SQL and Cosmos import tests**

```powershell
dotnet test .\src\Microsoft.Health.Fhir.SqlServer.UnitTests\Microsoft.Health.Fhir.SqlServer.UnitTests.csproj -f net9.0 --filter "FullyQualifiedName~ImportProcessingJobTests|FullyQualifiedName~ImportOrchestratorJobTests"
dotnet test .\src\Microsoft.Health.Fhir.CosmosDb.UnitTests\Microsoft.Health.Fhir.CosmosDb.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~ResourceWrapperTests
```

Expected: existing downstream storage tests pass without production storage changes.

- [ ] **Step 6: Verify the commit set and worktree**

```powershell
git --no-pager log --oneline main..HEAD
git --no-pager status --short
git --no-pager diff --check main...HEAD
```

Expected:

- Commits are limited to the design, provider configuration, Firely extraction, Ignixa implementation, composition, and build wiring.
- Worktree is clean.
- No whitespace errors.

- [ ] **Step 7: Record the PR description**

Use this scope statement:

```markdown
## What changed

- Added a global `Firely|Ignixa` provider preference, defaulting to Firely.
- Extracted the existing `$import` parser into version-specific Firely provider projects.
- Added one net9-only multi-version Ignixa import provider.
- Wired only `IImportResourceParser` to provider selection.
- Preserved the existing `ImportResource`, `ResourceWrapper`, indexing, SQL, and Cosmos boundaries.

## Deliberately unchanged

HTTP formatters, persistence codecs, FHIRPath, search indexing, validation, bundles,
PATCH, conformance, terminology, XML, target frameworks, CI, and Firely package versions.

## Rollback

Set `FhirServer:CoreFeatures:FhirSdkProvider` to `Firely`. No stored-data migration is required.
```

Do not create the PR until the user requests it.
