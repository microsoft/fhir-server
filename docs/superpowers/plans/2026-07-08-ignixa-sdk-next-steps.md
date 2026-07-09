# Ignixa SDK Next Steps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Ignixa mergeable to `main` by proving complete Firely and Ignixa SDK modes across every runtime surface.

**Architecture:** Add one explicit SDK mode contract, use it to select SDK-specific providers at startup, then close every P0 runtime surface where Ignixa mode still falls back to Firely. Hybrid mode remains a rollout helper, not the merge gate.

**Tech Stack:** .NET 9, ASP.NET Core MVC formatters, Microsoft.Extensions.DependencyInjection, xUnit, NSubstitute, Firely SDK, Ignixa SDK.

---

## Scope Check

The approved spec covers multiple subsystems. This file is the master implementation plan and is intentionally split into independently shippable tasks. Execute tasks in order and commit after each task. If a task reveals an Ignixa SDK capability gap, do not silently defer it; record it in the shim register and keep the corresponding P0 acceptance test failing or explicitly blocked until the product decision is made.

## File Structure

Create or modify these files.

### SDK mode contract

- Create `src/Microsoft.Health.Fhir.Core/Configs/SdkConfiguration.cs` — configuration object and enum for `Firely`, `Ignixa`, and `Hybrid`.
- Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkModeProvider.cs` — single source of truth for the active mode.
- Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkModeProvider.cs` — validates and exposes the active mode.
- Modify `src/Microsoft.Health.Fhir.Api/Configs/FhirServerConfiguration.cs` — add `Sdk` configuration property.
- Test `src/Microsoft.Health.Fhir.Core.UnitTests/Config/SdkConfigurationTests.cs`.

### Mode-aware startup and providers

- Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` — register Firely services always, then conditionally register Ignixa serializer/formatters/FHIRPath by mode.
- Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/ValidationModule.cs` — select `ModelAttributeValidator` vs `IgnixaResourceValidator` by mode.
- Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/SearchModule.cs` — keep Firely provider in Firely mode, replace with Ignixa provider in Ignixa/Hybrid modes.
- Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/PersistenceModule.cs` — select explicit raw resource factory by mode.
- Modify `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/ServiceCollectionExtensions.cs` — split base Ignixa services from active MVC formatter registration if needed.
- Create `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/SdkModeModuleTests.cs` — module-level DI assertions imported by `src/Microsoft.Health.Fhir.Api.UnitTests/Microsoft.Health.Fhir.R4.Api.UnitTests.csproj`.
- Modify `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems` — include the new module test file.

### Explicit persistence selection

- Create `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/FirelyRawResourceFactory.cs` — Firely-only implementation of `IRawResourceFactory`.
- Create `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/IgnixaModeRawResourceFactory.cs` — Ignixa-mode implementation of `IRawResourceFactory` that fails on non-Ignixa resources instead of silently serializing through Firely.
- Modify `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/RawResourceFactory.cs` — keep as Hybrid implementation or rename responsibility through DI registration.
- Create `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Persistence/SdkModeRawResourceFactoryTests.cs` — shared persistence tests imported by `src/Microsoft.Health.Fhir.R4.Core.UnitTests/Microsoft.Health.Fhir.R4.Core.UnitTests.csproj`.
- Modify `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems` — include the new persistence test file.

### Fallback and shim guardrails

- Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkFallbackGuard.cs`.
- Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkFallbackGuard.cs`.
- Create `docs/ignixa/shim-register.md`.
- Modify fallback surfaces to call the guard: `IgnixaFhirJsonOutputFormatter`, `IgnixaResourceValidator`, PATCH helper path, persistence fallback, search converter adapter.
- Test `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Sdk/SdkFallbackGuardTests.cs`.

### P0 runtime closures

- Modify `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/IgnixaFhirJsonOutputFormatter.cs` for `_summary`/`_elements` Ignixa-mode projection.
- Modify PATCH implementation under `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/` and controller payload wiring in `src/Microsoft.Health.Fhir.Shared.Api/Controllers/FhirController.cs`.
- Modify `src/Microsoft.Health.Fhir.Shared.Core/Features/Validation/IgnixaResourceValidator.cs` for conformance validation closure or explicit blocking.
- Modify import/export/bulk serializers under `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Import/`, `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Export/`, and bulk update files under `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Upsert/`.
- Add or extend E2E tests under `test/Microsoft.Health.Fhir.Shared.Tests.E2E/` and include them in `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Microsoft.Health.Fhir.Shared.Tests.E2E.projitems`.

## Task 1: Add SDK mode configuration and provider

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Configs/SdkConfiguration.cs`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkModeProvider.cs`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkModeProvider.cs`
- Modify: `src/Microsoft.Health.Fhir.Api/Configs/FhirServerConfiguration.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Config/SdkConfigurationTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Microsoft.Health.Fhir.Core.UnitTests/Config/SdkConfigurationTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class SdkConfigurationTests
{
    [Fact]
    public void GivenDefaultConfiguration_WhenModeProviderIsCreated_ThenHybridModeIsReturned()
    {
        var configuration = new SdkConfiguration();

        var provider = new SdkModeProvider(configuration);

        Assert.Equal(FhirSdkMode.Hybrid, provider.Mode);
        Assert.False(provider.IsFirelyMode);
        Assert.False(provider.IsIgnixaMode);
        Assert.True(provider.IsHybridMode);
    }

    [Theory]
    [InlineData(FhirSdkMode.Firely)]
    [InlineData(FhirSdkMode.Ignixa)]
    [InlineData(FhirSdkMode.Hybrid)]
    public void GivenSupportedMode_WhenModeProviderIsCreated_ThenModeIsReturned(FhirSdkMode mode)
    {
        var configuration = new SdkConfiguration { Mode = mode };

        var provider = new SdkModeProvider(configuration);

        Assert.Equal(mode, provider.Mode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkConfigurationTests" --no-restore
```

Expected: FAIL because `SdkConfiguration`, `FhirSdkMode`, `ISdkModeProvider`, and `SdkModeProvider` do not exist.

- [ ] **Step 3: Add configuration and provider types**

Create `src/Microsoft.Health.Fhir.Core/Configs/SdkConfiguration.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs;

public class SdkConfiguration
{
    public FhirSdkMode Mode { get; set; } = FhirSdkMode.Hybrid;
}

public enum FhirSdkMode
{
    Firely,
    Ignixa,
    Hybrid,
}
```

Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkModeProvider.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Sdk;

public interface ISdkModeProvider
{
    FhirSdkMode Mode { get; }

    bool IsFirelyMode { get; }

    bool IsIgnixaMode { get; }

    bool IsHybridMode { get; }
}
```

Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkModeProvider.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Sdk;

public class SdkModeProvider : ISdkModeProvider
{
    public SdkModeProvider(SdkConfiguration configuration)
    {
        EnsureArg.IsNotNull(configuration, nameof(configuration));

        if (!Enum.IsDefined(typeof(FhirSdkMode), configuration.Mode))
        {
            throw new InvalidOperationException($"Unsupported FHIR SDK mode: {configuration.Mode}.");
        }

        Mode = configuration.Mode;
    }

    public FhirSdkMode Mode { get; }

    public bool IsFirelyMode => Mode == FhirSdkMode.Firely;

    public bool IsIgnixaMode => Mode == FhirSdkMode.Ignixa;

    public bool IsHybridMode => Mode == FhirSdkMode.Hybrid;
}
```

Modify `src/Microsoft.Health.Fhir.Api/Configs/FhirServerConfiguration.cs`:

```csharp
public SdkConfiguration Sdk { get; } = new SdkConfiguration();
```

Place it near the other top-level configuration properties.

- [ ] **Step 4: Register provider in startup**

Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` near the top of `Load` after `EnsureArg`:

```csharp
services.AddSingleton(_ => _configuration.Sdk);
services.AddSingleton<ISdkModeProvider, SdkModeProvider>();
```

`FhirModule` currently has no constructor. Add one:

```csharp
private readonly FhirServerConfiguration _configuration;

public FhirModule(FhirServerConfiguration configuration)
{
    _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration));
}
```

Add `using Microsoft.Health.Fhir.Api.Configs;`, `using Microsoft.Health.Fhir.Core.Configs;`, and `using Microsoft.Health.Fhir.Core.Features.Sdk;` if needed.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkConfigurationTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core\Configs\SdkConfiguration.cs src\Microsoft.Health.Fhir.Core\Features\Sdk\ISdkModeProvider.cs src\Microsoft.Health.Fhir.Core\Features\Sdk\SdkModeProvider.cs src\Microsoft.Health.Fhir.Api\Configs\FhirServerConfiguration.cs src\Microsoft.Health.Fhir.Shared.Api\Modules\FhirModule.cs src\Microsoft.Health.Fhir.Core.UnitTests\Config\SdkConfigurationTests.cs
git commit -m "Add FHIR SDK mode configuration" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Make formatter and FHIRPath registration mode-aware

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/ServiceCollectionExtensions.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/SdkModeModuleTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems`

- [ ] **Step 1: Write failing DI tests and include them in the R4 API test project**

Create `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/SdkModeModuleTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Modules;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class SdkModeModuleTests
{
    [Fact]
    public void GivenFirelyMode_WhenFhirModuleLoads_ThenFirelyFormattersAndFhirPathRemainActive()
    {
        using var serviceProvider = BuildServiceProvider(FhirSdkMode.Firely);

        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;
        var fhirPathProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();

        Assert.DoesNotContain(mvcOptions.InputFormatters, x => x.GetType().Name == "IgnixaFhirJsonInputFormatter");
        Assert.DoesNotContain(mvcOptions.OutputFormatters, x => x.GetType().Name == "IgnixaFhirJsonOutputFormatter");
        Assert.Contains(mvcOptions.InputFormatters, x => x is FhirJsonInputFormatter);
        Assert.Contains(mvcOptions.OutputFormatters, x => x is FhirJsonOutputFormatter);
        Assert.IsType<FirelyFhirPathProvider>(fhirPathProvider);
    }

    [Fact]
    public void GivenIgnixaMode_WhenFhirModuleLoads_ThenIgnixaFormattersAndFhirPathAreActive()
    {
        using var serviceProvider = BuildServiceProvider(FhirSdkMode.Ignixa);

        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;
        var fhirPathProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();

        Assert.Equal("IgnixaFhirJsonInputFormatter", mvcOptions.InputFormatters[0].GetType().Name);
        Assert.Equal("IgnixaFhirJsonOutputFormatter", mvcOptions.OutputFormatters[0].GetType().Name);
        Assert.IsType<IgnixaFhirPathProvider>(fhirPathProvider);
    }

    private static ServiceProvider BuildServiceProvider(FhirSdkMode mode)
    {
        var configuration = new FhirServerConfiguration();
        configuration.Sdk.Mode = mode;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddMvcCore();
        new SearchModule(configuration).Load(services);
        new FhirModule(configuration).Load(services);

        return services.BuildServiceProvider(validateScopes: true);
    }
}
```

Modify `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems` and add this item near the other `<Compile Include=...>` entries:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Modules\SdkModeModuleTests.cs" />
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeModuleTests" --no-restore
```

Expected: FAIL because `FhirModule` has no constructor that accepts `FhirServerConfiguration` and still registers Ignixa formatters and FHIRPath unconditionally.

- [ ] **Step 3: Split Ignixa registration by active mode**

In `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs`, replace unconditional calls:

```csharp
services.AddIgnixaFhirPath(provider => provider.GetRequiredService<IIgnixaSchemaContext>().Schema);
services.AddIgnixaSerializationWithFormatters();
```

with:

```csharp
var sdkMode = _configuration.Sdk.Mode;
var useIgnixaActiveProviders = sdkMode == FhirSdkMode.Ignixa || sdkMode == FhirSdkMode.Hybrid;

if (useIgnixaActiveProviders)
{
    services.AddIgnixaFhirPath(provider => provider.GetRequiredService<IIgnixaSchemaContext>().Schema);
    services.AddIgnixaSerializationWithFormatters();
}
else
{
    services.AddIgnixaSerialization();
}
```

Use `AddIgnixaSerialization()` in Firely mode only if constructor dependencies like `RawResourceFactory` still require `IIgnixaJsonSerializer` before Task 4. After Task 4 removes Firely-mode Ignixa dependencies, tighten this so Firely mode does not register Ignixa serialization at all.

- [ ] **Step 4: Run focused module tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeModuleTests" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Api\Modules\FhirModule.cs src\Microsoft.Health.Fhir.Shared.Api.UnitTests\Modules\SdkModeModuleTests.cs src\Microsoft.Health.Fhir.Shared.Api.UnitTests\Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems
git commit -m "Make Ignixa formatter registration mode-aware" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Make validation registration mode-aware

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/ValidationModule.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Modules/SdkModeModuleTests.cs`

- [ ] **Step 1: Add failing validation provider tests**

Append to `SdkModeModuleTests`:

```csharp
[Fact]
public void GivenFirelyMode_WhenValidationModuleLoads_ThenFirelyValidatorIsActive()
{
    using var serviceProvider = BuildValidationServiceProvider(FhirSdkMode.Firely);

    var validator = serviceProvider.GetRequiredService<IModelAttributeValidator>();

    Assert.IsType<ModelAttributeValidator>(validator);
}

[Fact]
public void GivenIgnixaMode_WhenValidationModuleLoads_ThenIgnixaValidatorIsActive()
{
    using var serviceProvider = BuildValidationServiceProvider(FhirSdkMode.Ignixa);

    var validator = serviceProvider.GetRequiredService<IModelAttributeValidator>();

    Assert.IsType<IgnixaResourceValidator>(validator);
}

private static ServiceProvider BuildValidationServiceProvider(FhirSdkMode mode)
{
    var configuration = new FhirServerConfiguration();
    configuration.Sdk.Mode = mode;

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddOptions();
    services.AddMvcCore();
    new FhirModule(configuration).Load(services);
    new ValidationModule(configuration).Load(services);

    return services.BuildServiceProvider(validateScopes: true);
}
```

Add `using Microsoft.Health.Fhir.Core.Features.Validation;` at the top of the file.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeModuleTests" --no-restore
```

Expected: FAIL because `ValidationModule` always registers `IgnixaResourceValidator`.

- [ ] **Step 3: Add configuration to ValidationModule**

Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/ValidationModule.cs`:

```csharp
private readonly FhirServerConfiguration _configuration;

public ValidationModule(FhirServerConfiguration configuration)
{
    _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration));
}
```

Replace the current `IModelAttributeValidator` registration with:

```csharp
services.AddSingleton<ModelAttributeValidator>();

if (_configuration.Sdk.Mode == FhirSdkMode.Firely)
{
    services.AddSingleton<IModelAttributeValidator>(sp => sp.GetRequiredService<ModelAttributeValidator>());
}
else
{
    services.AddSingleton<IModelAttributeValidator>(sp =>
    {
        var schemaContext = sp.GetRequiredService<IIgnixaSchemaContext>();
        var fallbackValidator = sp.GetRequiredService<ModelAttributeValidator>();
        return new IgnixaResourceValidator(schemaContext, fallbackValidator);
    });
}
```

Add `using Microsoft.Health.Fhir.Api.Configs;` and `using Microsoft.Health.Fhir.Core.Configs;`.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeModuleTests" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Api\Modules\ValidationModule.cs src\Microsoft.Health.Fhir.Shared.Api.UnitTests\Modules\SdkModeModuleTests.cs
git commit -m "Make validation provider registration mode-aware" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Make persistence read/write mode explicit

**Files:**
- Create: `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/FirelyRawResourceFactory.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/IgnixaModeRawResourceFactory.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/PersistenceModule.cs`
- Create: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Persistence/SdkModeRawResourceFactoryTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems`

- [ ] **Step 1: Write failing factory tests**

Create `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Persistence/SdkModeRawResourceFactoryTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Persistence)]
public class SdkModeRawResourceFactoryTests
{
    [Fact]
    public void GivenFirelyFactory_WhenCreatingRawResource_ThenFirelyJsonIsProduced()
    {
        var patient = new Patient { Id = "firely-patient", Active = true };
        var factory = new FirelyRawResourceFactory(new FhirJsonSerializer());

        var raw = factory.Create(patient.ToResourceElement(), keepMeta: true);

        Assert.Equal(FhirResourceFormat.Json, raw.Format);
        Assert.Contains("\"resourceType\":\"Patient\"", raw.Data);
        Assert.Contains("\"id\":\"firely-patient\"", raw.Data);
    }

    [Fact]
    public void GivenIgnixaFactoryAndIgnixaResource_WhenCreatingRawResource_ThenIgnixaJsonIsProduced()
    {
        var serializer = new IgnixaJsonSerializer();
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        var node = serializer.Parse("{\"resourceType\":\"Patient\",\"id\":\"ignixa-patient\",\"active\":true}");
        var element = new IgnixaResourceElement(node, schemaContext.Schema).ToResourceElement();
        var factory = new IgnixaModeRawResourceFactory(serializer);

        var raw = factory.Create(element, keepMeta: true);

        Assert.Equal(FhirResourceFormat.Json, raw.Format);
        Assert.Contains("\"resourceType\":\"Patient\"", raw.Data);
        Assert.Contains("\"id\":\"ignixa-patient\"", raw.Data);
    }

    [Fact]
    public void GivenIgnixaFactoryAndFirelyResource_WhenCreatingRawResource_ThenInvalidOperationExceptionIsThrown()
    {
        var patient = new Patient { Id = "firely-patient" };
        var factory = new IgnixaModeRawResourceFactory(new IgnixaJsonSerializer());

        Assert.Throws<InvalidOperationException>(() => factory.Create(patient.ToResourceElement(), keepMeta: true));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeRawResourceFactoryTests" --no-restore
```

Expected: FAIL because `FirelyRawResourceFactory` and `IgnixaModeRawResourceFactory` do not exist.

- [ ] **Step 3: Add explicit factories**

Create `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/FirelyRawResourceFactory.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public class FirelyRawResourceFactory : IRawResourceFactory
{
    private readonly FhirJsonSerializer _serializer;

    public FirelyRawResourceFactory(FhirJsonSerializer serializer)
    {
        _serializer = EnsureArg.IsNotNull(serializer, nameof(serializer));
    }

    public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        var poco = resource.ToPoco<Resource>();
        poco.Meta ??= new Meta();
        var versionId = poco.Meta.VersionId;

        try
        {
            if (!keepMeta)
            {
                poco.Meta.VersionId = null;
            }
            else if (!keepVersion)
            {
                poco.Meta.VersionId = "1";
            }

            return new RawResource(_serializer.SerializeToString(poco), FhirResourceFormat.Json, keepMeta);
        }
        finally
        {
            if (!keepMeta)
            {
                poco.Meta.VersionId = versionId;
            }
        }
    }
}
```

Create `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/IgnixaModeRawResourceFactory.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public class IgnixaModeRawResourceFactory : IRawResourceFactory
{
    private readonly IIgnixaJsonSerializer _serializer;

    public IgnixaModeRawResourceFactory(IIgnixaJsonSerializer serializer)
    {
        _serializer = EnsureArg.IsNotNull(serializer, nameof(serializer));
    }

    public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        var resourceNode = resource.GetIgnixaNode();
        if (resourceNode == null)
        {
            throw new InvalidOperationException("Ignixa SDK mode cannot persist a resource that does not carry an Ignixa ResourceJsonNode.");
        }

        var originalVersionId = resourceNode.Meta?.VersionId;
        try
        {
            if (!keepMeta && resourceNode.Meta != null)
            {
                resourceNode.Meta.VersionId = null;
            }
            else if (!keepVersion && resourceNode.Meta != null)
            {
                resourceNode.Meta.VersionId = "1";
            }

            return new RawResource(_serializer.Serialize(resourceNode), FhirResourceFormat.Json, keepMeta);
        }
        finally
        {
            if (!keepMeta && resourceNode.Meta != null)
            {
                resourceNode.Meta.VersionId = originalVersionId;
            }
        }
    }
}
```

- [ ] **Step 4: Select factory in PersistenceModule**

Modify `src/Microsoft.Health.Fhir.Shared.Api/Modules/PersistenceModule.cs` to accept `FhirServerConfiguration` and register:

```csharp
if (_configuration.Sdk.Mode == FhirSdkMode.Firely)
{
    services.AddSingleton<IRawResourceFactory, FirelyRawResourceFactory>();
}
else if (_configuration.Sdk.Mode == FhirSdkMode.Ignixa)
{
    services.AddSingleton<IRawResourceFactory, IgnixaModeRawResourceFactory>();
}
else
{
    services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
}
```

- [ ] **Step 5: Include the new shared core persistence tests**

Modify `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems` and add this item near the other persistence tests:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Features\Persistence\SdkModeRawResourceFactoryTests.cs" />
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeRawResourceFactoryTests" --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Features\Persistence\FirelyRawResourceFactory.cs src\Microsoft.Health.Fhir.Shared.Core\Features\Persistence\IgnixaModeRawResourceFactory.cs src\Microsoft.Health.Fhir.Shared.Api\Modules\PersistenceModule.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Persistence\SdkModeRawResourceFactoryTests.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems
git commit -m "Make raw resource persistence mode explicit" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Add fallback guard and shim register

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkFallbackGuard.cs`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkFallbackGuard.cs`
- Create: `docs/ignixa/shim-register.md`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Sdk/SdkFallbackGuardTests.cs`

- [ ] **Step 1: Write failing guard tests**

Create `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Sdk/SdkFallbackGuardTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Sdk;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class SdkFallbackGuardTests
{
    [Fact]
    public void GivenIgnixaMode_WhenFirelyFallbackIsRequested_ThenInvalidOperationExceptionIsThrown()
    {
        var mode = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Ignixa });
        var guard = new SdkFallbackGuard(mode, NullLogger<SdkFallbackGuard>.Instance);

        Assert.Throws<InvalidOperationException>(() => guard.FirelyFallback("projection", "summary projection"));
    }

    [Fact]
    public void GivenHybridMode_WhenFirelyFallbackIsRequested_ThenNoExceptionIsThrown()
    {
        var mode = new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Hybrid });
        var guard = new SdkFallbackGuard(mode, NullLogger<SdkFallbackGuard>.Instance);

        guard.FirelyFallback("projection", "summary projection");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkFallbackGuardTests" --no-restore
```

Expected: FAIL because fallback guard types do not exist.

- [ ] **Step 3: Implement fallback guard**

Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/ISdkFallbackGuard.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Sdk;

public interface ISdkFallbackGuard
{
    void FirelyFallback(string surface, string reason);

    void IgnixaFallback(string surface, string reason);
}
```

Create `src/Microsoft.Health.Fhir.Core/Features/Sdk/SdkFallbackGuard.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Sdk;

public class SdkFallbackGuard : ISdkFallbackGuard
{
    private readonly ISdkModeProvider _modeProvider;
    private readonly ILogger<SdkFallbackGuard> _logger;

    public SdkFallbackGuard(ISdkModeProvider modeProvider, ILogger<SdkFallbackGuard> logger)
    {
        _modeProvider = EnsureArg.IsNotNull(modeProvider, nameof(modeProvider));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    public void FirelyFallback(string surface, string reason)
    {
        EnsureArg.IsNotNullOrWhiteSpace(surface, nameof(surface));
        EnsureArg.IsNotNullOrWhiteSpace(reason, nameof(reason));

        if (_modeProvider.IsIgnixaMode)
        {
            throw new InvalidOperationException($"Firely fallback is not allowed in Ignixa SDK mode. Surface: {surface}. Reason: {reason}.");
        }

        _logger.LogInformation("Firely SDK fallback used. Surface: {Surface}. Reason: {Reason}. Mode: {Mode}.", surface, reason, _modeProvider.Mode);
    }

    public void IgnixaFallback(string surface, string reason)
    {
        EnsureArg.IsNotNullOrWhiteSpace(surface, nameof(surface));
        EnsureArg.IsNotNullOrWhiteSpace(reason, nameof(reason));

        if (_modeProvider.IsFirelyMode)
        {
            throw new InvalidOperationException($"Ignixa fallback is not allowed in Firely SDK mode. Surface: {surface}. Reason: {reason}.");
        }

        _logger.LogInformation("Ignixa SDK fallback used. Surface: {Surface}. Reason: {Reason}. Mode: {Mode}.", surface, reason, _modeProvider.Mode);
    }
}
```

Register it where `ISdkModeProvider` is registered:

```csharp
services.AddSingleton<ISdkFallbackGuard, SdkFallbackGuard>();
```

- [ ] **Step 4: Create shim register**

Create `docs/ignixa/shim-register.md`:

```markdown
# Ignixa Shim Register

This register is required for merge readiness. Any bridge between Firely and Ignixa must be listed here unless it is removed.

| ID | Surface | File | Allowed mode | Reason | Severity | Owner | Removal condition | Test |
|---|---|---|---|---|---|---|---|---|
| SHIM-PROJECTION-001 | Output projection | `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/IgnixaFhirJsonOutputFormatter.cs` | Hybrid only | `_summary` and `_elements` currently use Firely projection | P0 | FHIR | Implement Ignixa-native projection | `IgnixaFhirJsonOutputFormatterTests` projection tests fail in Ignixa mode until closed |
| SHIM-CONFORMANCE-001 | Validation | `src/Microsoft.Health.Fhir.Shared.Core/Features/Validation/IgnixaResourceValidator.cs` | Hybrid only | Conformance resources currently use Firely validation | P0 | FHIR | Implement or explicitly block conformance validation in Ignixa mode | `IgnixaResourceValidatorTests` conformance tests |
| SHIM-PATCH-001 | PATCH | `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/` | Hybrid only | FHIRPath PATCH currently depends on Firely model infrastructure | P0 | FHIR | Implement Ignixa-native PATCH path | PATCH E2E tests in Ignixa mode |
| SHIM-SEARCH-001 | Search value conversion | `src/Microsoft.Health.Fhir.Core/Features/Search/Converters/` | Hybrid and Ignixa | Search converter boundary still uses `ITypedElement` | P1 | FHIR | Add SDK-aware converter seam or approve adapter as supported infrastructure | Search indexing tests in both modes |
```

- [ ] **Step 5: Run guard tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkFallbackGuardTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core\Features\Sdk\ISdkFallbackGuard.cs src\Microsoft.Health.Fhir.Core\Features\Sdk\SdkFallbackGuard.cs src\Microsoft.Health.Fhir.Shared.Api\Modules\FhirModule.cs src\Microsoft.Health.Fhir.Core.UnitTests\Features\Sdk\SdkFallbackGuardTests.cs docs\ignixa\shim-register.md
git commit -m "Add SDK fallback guard and shim register" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Wire guard into known fallback points

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/IgnixaFhirJsonOutputFormatter.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Validation/IgnixaResourceValidator.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/RawResourceFactory.cs`
- Test: existing formatter and validator tests.

- [ ] **Step 1: Add failing fallback tests**

Add to `IgnixaFhirJsonOutputFormatterTests`:

```csharp
[Fact]
public async Task GivenIgnixaModeAndProjectionFallback_WhenWriting_ThenInvalidOperationExceptionIsThrown()
{
    var guard = new SdkFallbackGuard(
        new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Ignixa }),
        NullLogger<SdkFallbackGuard>.Instance);
    var formatter = new IgnixaFhirJsonOutputFormatter(_ignixaSerializer, new FhirJsonSerializer(), ModelInfoProvider.Instance, guard);
    var patient = new Patient { Id = "projection-block", Active = true };
    var node = _ignixaSerializer.Parse(patient.ToJson());

    await Assert.ThrowsAsync<InvalidOperationException>(() => WriteObject(formatter, node, typeof(ResourceJsonNode), "?_elements=active"));
}
```

Update the existing `WriteObject` helper to delegate to an overload that accepts a formatter instance:

```csharp
private Task<string> WriteObject(object obj, Type objectType, string query = null)
{
    return WriteObject(_formatter, obj, objectType, query);
}

private async Task<string> WriteObject(IgnixaFhirJsonOutputFormatter formatter, object obj, Type objectType, string query = null)
{
    using var body = new MemoryStream();
    var httpContext = new DefaultHttpContext();
    httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
    httpContext.Response.Body = body;
    if (query != null)
    {
        httpContext.Request.QueryString = new QueryString(query);
    }

    using var writer = new StringWriter();
    var writeContext = new OutputFormatterWriteContext(
        httpContext,
        (_, _) => writer,
        objectType,
        obj);

    await formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

    body.Seek(0, SeekOrigin.Begin);
    using var reader = new StreamReader(body);
    return await reader.ReadToEndAsync();
}
```

This replaces the current helper body at the bottom of `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Ignixa/IgnixaFhirJsonOutputFormatterTests.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaFhirJsonOutputFormatterTests" --no-restore
```

Expected: FAIL because formatter does not accept or call `ISdkFallbackGuard`.

- [ ] **Step 3: Inject and call guard in output formatter**

Modify `IgnixaFhirJsonOutputFormatter` constructor:

```csharp
private readonly ISdkFallbackGuard _fallbackGuard;

public IgnixaFhirJsonOutputFormatter(
    IIgnixaJsonSerializer serializer,
    FhirJsonSerializer firelySerializer,
    IModelInfoProvider modelInfoProvider,
    ISdkFallbackGuard fallbackGuard)
{
    _serializer = EnsureArg.IsNotNull(serializer, nameof(serializer));
    _firelySerializer = EnsureArg.IsNotNull(firelySerializer, nameof(firelySerializer));
    _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
    _fallbackGuard = EnsureArg.IsNotNull(fallbackGuard, nameof(fallbackGuard));
}
```

Before Firely projection fallback:

```csharp
if (hasProjection)
{
    _fallbackGuard.FirelyFallback("Ignixa output projection", "_summary or _elements projection is not implemented natively on ResourceJsonNode.");
    var firelyResource = await ToFirelyResourceAsync(resourceNode).ConfigureAwait(false);
    await WriteFirelyResourceAsync(firelyResource, response, pretty, selectedEncoding, summarySearchParameter, GetProjectedElements(firelyResource, elementsSearchParameter)).ConfigureAwait(false);
    return;
}
```

Before Firely `Resource` serialization in Ignixa formatter:

```csharp
else if (context.Object is Resource firelyResource)
{
    _fallbackGuard.FirelyFallback("Ignixa output formatter Firely resource", "Response object is a Firely Resource.");
    await WriteFirelyResourceAsync(firelyResource, response, pretty, selectedEncoding, summarySearchParameter, GetProjectedElements(firelyResource, elementsSearchParameter)).ConfigureAwait(false);
    return;
}
```

Only add this second guard if `Ignixa` mode is intended to reject Firely response objects. If OperationOutcome and CapabilityStatement still intentionally use Firely in Ignixa mode, add those to `docs/ignixa/shim-register.md` as P0/P1 entries and keep `Hybrid` as the only allowed fallback mode.

- [ ] **Step 4: Guard conformance fallback**

Modify `IgnixaResourceValidator` constructor to accept `ISdkFallbackGuard`, store it, and call:

```csharp
_fallbackGuard.FirelyFallback("Ignixa resource validation", $"Conformance resource validation for {resourceType} uses Firely.");
```

immediately before conformance fallback.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaFhirJsonOutputFormatterTests|FullyQualifiedName~IgnixaResourceValidatorTests" --no-restore
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~SdkFallbackGuardTests" --no-restore
```

Expected: PASS after updating test constructors.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Ignixa\IgnixaFhirJsonOutputFormatter.cs src\Microsoft.Health.Fhir.Shared.Core\Features\Validation\IgnixaResourceValidator.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Ignixa\IgnixaFhirJsonOutputFormatterTests.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Validation\IgnixaResourceValidatorTests.cs docs\ignixa\shim-register.md
git commit -m "Block hidden Firely fallbacks in Ignixa mode" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: Add mode-specific runtime surface test matrix

**Files:**
- Create: `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Mode/SdkModeRuntimeSurfaceTests.cs`
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Microsoft.Health.Fhir.Shared.Tests.E2E.projitems`
- Modify E2E host configuration files used by R4/STU3/R4B/R5 test projects.

- [ ] **Step 1: Create shared E2E test cases**

Create `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Mode/SdkModeRuntimeSurfaceTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Mode;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class SdkModeRuntimeSurfaceTests<TStartup> : IClassFixture<HttpIntegrationTestFixture<TStartup>>
    where TStartup : class
{
    private readonly TestFhirClient _client;

    public SdkModeRuntimeSurfaceTests(HttpIntegrationTestFixture<TStartup> fixture)
    {
        _client = fixture.TestFhirClient;
    }

    [Fact]
    public async Task GivenSelectedSdkMode_WhenRunningCrudSearchAndProjection_ThenRuntimeSurfaceSucceeds()
    {
        var patient = new Patient
        {
            Active = true,
            Name = { new HumanName { Family = "SdkMode", Given = new[] { "Runtime" } } },
        };

        using FhirResponse<Patient> createResponse = await _client.CreateAsync(patient);
        Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);

        using FhirResponse<Patient> readResponse = await _client.ReadAsync<Patient>(ResourceType.Patient, createResponse.Resource.Id);
        Assert.Equal(System.Net.HttpStatusCode.OK, readResponse.StatusCode);

        using FhirResponse<Bundle> searchResponse = await _client.SearchAsync(ResourceType.Patient, $"_id={createResponse.Resource.Id}&_elements=active");
        Assert.Equal(System.Net.HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.NotEmpty(searchResponse.Resource.Entry);

        createResponse.Resource.Active = false;
        using FhirResponse<Patient> updateResponse = await _client.UpdateAsync(createResponse.Resource);
        Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

        using FhirResponse deleteResponse = await _client.DeleteAsync(createResponse.Resource);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
```

- [ ] **Step 2: Wire mode-specific E2E execution**

Modify `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Microsoft.Health.Fhir.Shared.Tests.E2E.projitems` and add:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Mode\SdkModeRuntimeSurfaceTests.cs" />
```

Add two CI/test configurations:

```powershell
$env:FhirServer__Sdk__Mode = "Firely"
dotnet test .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeRuntimeSurfaceTests" --no-restore

$env:FhirServer__Sdk__Mode = "Ignixa"
dotnet test .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --framework net9.0 --filter "FullyQualifiedName~SdkModeRuntimeSurfaceTests" --no-restore
```

Use the repo's existing E2E configuration mechanism if it names environment variables differently. The important point is that both modes run as separate jobs or separate test invocations.

- [ ] **Step 3: Run local compile**

Run:

```powershell
dotnet build .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add test\Microsoft.Health.Fhir.Shared.Tests.E2E\Mode\SdkModeRuntimeSurfaceTests.cs test\Microsoft.Health.Fhir.Shared.Tests.E2E\Microsoft.Health.Fhir.Shared.Tests.E2E.projitems
git commit -m "Add SDK mode runtime surface E2E scaffold" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 8: Close `_summary` and `_elements` projection fallback

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Ignixa/IgnixaFhirJsonOutputFormatter.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Ignixa/IgnixaFhirJsonOutputFormatterTests.cs`

- [ ] **Step 1: Write failing Ignixa-mode projection tests**

Add tests that run with `SdkFallbackGuard` in Ignixa mode and expect success:

```csharp
[Theory]
[InlineData("?_elements=active")]
[InlineData("?_summary=data")]
public async Task GivenIgnixaModeAndProjection_WhenWritingIgnixaResource_ThenProjectionIsAppliedWithoutFirelyFallback(string query)
{
    var guard = new SdkFallbackGuard(
        new SdkModeProvider(new SdkConfiguration { Mode = FhirSdkMode.Ignixa }),
        NullLogger<SdkFallbackGuard>.Instance);
    var formatter = new IgnixaFhirJsonOutputFormatter(_ignixaSerializer, new FhirJsonSerializer(), ModelInfoProvider.Instance, guard);
    var patient = new Patient
    {
        Id = "projection-test",
        Active = true,
        Name = { new HumanName { Family = "Hidden" } },
    };
    var node = _ignixaSerializer.Parse(patient.ToJson());

    var json = await WriteObject(formatter, node, typeof(ResourceJsonNode), query);
    var parsed = Parser.Parse<Patient>(json);

    Assert.Equal("projection-test", parsed.Id);
    Assert.True(parsed.Active);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaFhirJsonOutputFormatterTests" --no-restore
```

Expected: FAIL because projection currently calls Firely fallback and the guard blocks it.

- [ ] **Step 3: Implement Ignixa projection**

Add an internal projection method in `IgnixaFhirJsonOutputFormatter` that clones the resource into a `System.Text.Json.Nodes.JsonObject`, keeps mandatory fields, and removes top-level fields that are not requested.

Minimum implementation target for first pass:

```csharp
private ResourceJsonNode ProjectResource(ResourceJsonNode resourceNode, string[]? elements, SummaryType summaryType)
{
    var json = _serializer.Serialize(resourceNode, pretty: false);
    var jsonObject = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("Ignixa projection requires a JSON object resource.");

    var allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "resourceType",
        "id",
        "meta",
    };

    if (elements != null)
    {
        foreach (var element in elements)
        {
            allowed.Add(element);
        }
    }

    if (summaryType == SummaryType.Data)
    {
        allowed.Add("text");
        allowed.Add("extension");
    }

    foreach (var propertyName in jsonObject.Select(x => x.Key).ToList())
    {
        if (!allowed.Contains(propertyName))
        {
            jsonObject.Remove(propertyName);
        }
    }

    return _serializer.Parse(jsonObject.ToJsonString());
}
```

Use this method before serialization when `hasProjection` is true. This first pass covers top-level projection. Add nested projection support in a follow-up step in the same task before the task is complete.

- [ ] **Step 4: Add nested projection cases before completing**

Add test data and assertions for:

```csharp
[Theory]
[InlineData("name")]
[InlineData("name.family")]
[InlineData("telecom.value")]
[InlineData("extension")]
public async Task GivenElementsProjection_WhenNestedElementIsRequested_ThenRequestedElementIsPreserved(string element)
```

Implement recursive object/array filtering until these pass. Do not mark PATH-4 complete until nested elements and choice-type fields are covered.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaFhirJsonOutputFormatterTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Update shim register and commit**

Remove or mark `SHIM-PROJECTION-001` as closed in `docs/ignixa/shim-register.md`.

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Ignixa\IgnixaFhirJsonOutputFormatter.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Ignixa\IgnixaFhirJsonOutputFormatterTests.cs docs\ignixa\shim-register.md
git commit -m "Implement Ignixa projection output path" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 9: Close conformance validation fallback

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Validation/IgnixaResourceValidator.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Validation/IgnixaResourceValidatorTests.cs`

- [ ] **Step 1: Write failing conformance tests**

Add tests:

```csharp
[Theory]
[InlineData("StructureDefinition")]
[InlineData("SearchParameter")]
[InlineData("ValueSet")]
[InlineData("CodeSystem")]
public async Task GivenIgnixaModeAndConformanceResource_WhenValidating_ThenFirelyFallbackIsNotUsed(string resourceType)
{
    var validator = CreateIgnixaModeValidator();
    var resource = await CreateResourceElement(GetMinimalConformanceJson(resourceType));
    var results = new List<ValidationResult>();

    var isValid = validator.TryValidate(resource, results, recurse: false);

    Assert.True(isValid, string.Join("; ", results.Select(x => x.ErrorMessage)));
}
```

Add helper:

```csharp
private static string GetMinimalConformanceJson(string resourceType)
{
    return resourceType switch
    {
        "ValueSet" => "{\"resourceType\":\"ValueSet\",\"url\":\"http://example.org/vs\",\"status\":\"active\"}",
        "CodeSystem" => "{\"resourceType\":\"CodeSystem\",\"url\":\"http://example.org/cs\",\"status\":\"active\",\"content\":\"complete\"}",
        "SearchParameter" => "{\"resourceType\":\"SearchParameter\",\"url\":\"http://example.org/sp\",\"status\":\"active\",\"code\":\"x\",\"base\":[\"Patient\"],\"type\":\"string\",\"expression\":\"Patient.name\"}",
        "StructureDefinition" => "{\"resourceType\":\"StructureDefinition\",\"url\":\"http://example.org/sd\",\"status\":\"active\",\"name\":\"Example\",\"kind\":\"resource\",\"abstract\":false,\"type\":\"Patient\",\"baseDefinition\":\"http://hl7.org/fhir/StructureDefinition/Patient\",\"derivation\":\"constraint\"}",
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, null),
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaResourceValidatorTests" --no-restore
```

Expected: FAIL because conformance resources use Firely fallback and guard blocks in Ignixa mode.

- [ ] **Step 3: Replace hardcoded fallback with explicit strategy**

Use this decision tree in `IgnixaResourceValidator`:

```csharp
if (ConformanceResourceTypes.Contains(resourceType))
{
    if (_sdkModeProvider.IsIgnixaMode)
    {
        return TryValidateConformanceIgnixa(resourceNode, resourceType, validationResults);
    }

    _fallbackGuard.FirelyFallback("Ignixa resource validation", $"Conformance resource validation for {resourceType} uses Firely.");
    var ignixaElement = new IgnixaResourceElement(resourceNode, _schemaContext.Schema);
    return _fallbackValidator.TryValidate(ignixaElement.ToResourceElement(), validationResults, recurse);
}
```

Implement `TryValidateConformanceIgnixa` by building a validation schema from `_schemaContext.Schema.GetTypeDefinition(resourceType)` and running Ignixa validation with `ValidationDepth.Compatibility`, just like non-conformance resources. If Ignixa returns false for a resource that Firely accepts, keep the failing test and record the exact Ignixa SDK gap in `docs/ignixa/shim-register.md`.

- [ ] **Step 4: Run focused validation tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~IgnixaResourceValidatorTests" --no-restore
```

Expected: PASS or fail with a documented Ignixa SDK gap that blocks PATH-5.

- [ ] **Step 5: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Features\Validation\IgnixaResourceValidator.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Validation\IgnixaResourceValidatorTests.cs docs\ignixa\shim-register.md
git commit -m "Close Ignixa conformance validation fallback" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 10: Close PATCH Firely dependency

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/PatchResourceHandler.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatchPayload.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/FhirPathPatchBuilder.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationBase.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationAdd.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationReplace.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationDelete.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationMove.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationInsert.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationUpsert.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Helpers/ElementModelExtensions.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Controllers/FhirController.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/Patch/FhirPatchReplaceTests.cs`
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/FhirPathPatchTests.cs`

- [ ] **Step 1: Add failing unit coverage for Ignixa-backed resources**

Add this test to `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/Patch/FhirPatchReplaceTests.cs`:

```csharp
[Fact]
public void GivenIgnixaBackedPatient_WhenReplacingPrimitiveValue_ThenPatchedResourceKeepsIgnixaNode()
{
    var patchParam = new Parameters().AddReplacePatchParameter("Patient.active", new FhirBoolean(false));
    var serializer = new IgnixaJsonSerializer();
    var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
    var node = serializer.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
    var resource = new IgnixaResourceElement(node, schemaContext.Schema).ToResourceElement();

    var patched = new FhirPathPatchBuilder(resource, patchParam).Apply();

    Assert.NotNull(patched.GetIgnixaNode());
    var patient = patched.ToPoco<Patient>();
    Assert.False(patient.Active);
}
```

Add these `using` statements if they are not already present:

```csharp
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Ignixa;
```

- [ ] **Step 2: Add failing Ignixa-mode PATCH E2E coverage**

Add this test to `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/FhirPathPatchTests.cs`:

```csharp
[Fact]
public async Task GivenIgnixaMode_WhenPatchReplacesPatientActive_ThenPatchSucceedsWithoutFirelyFallback()
{
    var patient = new Patient { Active = true };
    using var create = await _client.CreateAsync(patient);

    var patch = new Parameters();
    patch.Parameter.Add(new Parameters.ParameterComponent
    {
        Name = "operation",
        Part =
        {
            new Parameters.ParameterComponent { Name = "type", Value = new Code("replace") },
            new Parameters.ParameterComponent { Name = "path", Value = new FhirString("Patient.active") },
            new Parameters.ParameterComponent { Name = "value", Value = new FhirBoolean(false) },
        },
    });

    using var response = await _client.PatchAsync<Patient>(ResourceType.Patient, create.Resource.Id, patch);

    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    Assert.False(response.Resource.Active);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~FhirPatchReplaceTests.GivenIgnixaBackedPatient_WhenReplacingPrimitiveValue_ThenPatchedResourceKeepsIgnixaNode" --no-restore
$env:FhirServer__Sdk__Mode = "Ignixa"
dotnet test .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --framework net9.0 --filter "FullyQualifiedName~FhirPathPatchTests.GivenIgnixaMode_WhenPatchReplacesPatientActive_ThenPatchSucceedsWithoutFirelyFallback" --no-restore
```

Expected: FAIL because PATCH uses Firely infrastructure or guard blocks Firely fallback.

- [ ] **Step 4: Implement Ignixa-native PATCH or record blocker**

Implement an Ignixa PATCH path if Ignixa supports the required operations. The path must:

- Apply add, replace, delete, move, copy.
- Preserve id, version, and meta behavior.
- Return `ResourceElement` carrying `ResourceJsonNode`.
- Avoid `ToPoco<Resource>()` in Ignixa mode.

If Ignixa cannot support one of the required operations, keep the failing test and record `SHIM-PATCH-001` as a P0 external blocker with exact operation coverage in `docs/ignixa/shim-register.md`.

- [ ] **Step 5: Run PATCH tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~FhirPatch" --no-restore
$env:FhirServer__Sdk__Mode = "Ignixa"
dotnet test .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --framework net9.0 --filter "FullyQualifiedName~FhirPathPatchTests" --no-restore
```

Expected: PASS if implementation is complete, or a documented P0 blocker if Ignixa SDK support is missing.

- [ ] **Step 6: Commit**

```powershell
git add src test docs\ignixa\shim-register.md
git commit -m "Close Ignixa PATCH runtime path" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 11: Align import, export, bulk update, and reindex with SDK mode

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Import/ImportResourceParser.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Operations/Export/ResourceToNdjsonBytesSerializer.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Upsert/BulkUpdateService.cs`
- Modify reindex job handlers found with `rg "Reindex" src/Microsoft.Health.Fhir.Shared.Core src/Microsoft.Health.Fhir.Core -n`
- Test: import/export/bulk/reindex unit and E2E tests.

- [ ] **Step 1: Add mode-specific bulk path tests**

Add tests that assert:

- Firely mode import/export works without Ignixa-active providers.
- Ignixa mode import/export preserves `ResourceJsonNode`.
- Bulk update in Ignixa mode writes updated resources through Ignixa-mode persistence.
- Reindex in Ignixa mode uses Ignixa `IFhirPathProvider`.

Use NSubstitute to assert provider calls:

```csharp
rawResourceFactory.Received().Create(Arg.Is<ResourceElement>(x => x.GetIgnixaNode() != null), Arg.Any<bool>(), Arg.Any<bool>());
```

- [ ] **Step 2: Run focused tests to verify failures**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~ImportResourceParserTests|FullyQualifiedName~ResourceToNdjsonBytesSerializerTests|FullyQualifiedName~BulkUpdateServiceTests|FullyQualifiedName~ReindexOrchestratorJobTests|FullyQualifiedName~ReindexProcessingJobTests|FullyQualifiedName~ReindexSingleResourceRequestHandlerTests" --no-restore
```

Expected: FAIL for mode assertions that are not implemented yet.

- [ ] **Step 3: Thread SDK mode/provider usage**

For import/export/bulk/reindex code:

- Inject `ISdkModeProvider` where the component chooses behavior.
- Do not read configuration directly from business logic.
- Prefer selected provider interfaces over `if` statements in handlers.
- If a handler must branch, keep the branch at the adapter boundary and test both modes.

Example adapter boundary:

```csharp
public interface IResourceJsonSerializer
{
    string Serialize(ResourceElement resourceElement);
}
```

Firely implementation serializes `resourceElement.Instance.ToJson()`. Ignixa implementation requires `resourceElement.GetIgnixaNode()` and throws in Ignixa mode when missing.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --filter "FullyQualifiedName~ImportResourceParserTests|FullyQualifiedName~ResourceToNdjsonBytesSerializerTests|FullyQualifiedName~BulkUpdateServiceTests|FullyQualifiedName~ReindexOrchestratorJobTests|FullyQualifiedName~ReindexProcessingJobTests|FullyQualifiedName~ReindexSingleResourceRequestHandlerTests" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src test docs\ignixa\shim-register.md
git commit -m "Align bulk resource codecs with SDK mode" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 12: Add observability and docs

**Files:**
- Modify fallback/provider surfaces touched earlier.
- Create: `docs/ignixa/sdk-mode-rollout.md`
- Modify: `docs/ignixa/shim-register.md`

- [ ] **Step 1: Add logging tests for fallback guard**

Extend `SdkFallbackGuardTests` with a test logger or verify exception messages include:

- Mode.
- Surface.
- Reason.

Example assertion:

```csharp
var exception = Assert.Throws<InvalidOperationException>(() => guard.FirelyFallback("projection", "native projection missing"));
Assert.Contains("projection", exception.Message);
Assert.Contains("native projection missing", exception.Message);
Assert.Contains("Ignixa SDK mode", exception.Message);
```

- [ ] **Step 2: Add rollout runbook**

Create `docs/ignixa/sdk-mode-rollout.md`:

```markdown
# SDK Mode Rollout

## Modes

| Mode | Use |
|---|---|
| Firely | Compatibility baseline and rollback |
| Ignixa | Target production mode |
| Hybrid | Migration and diagnosis only |

## Rollout

1. Deploy with `Firely`.
2. Run smoke tests for create, read, search, PATCH, import, export, bulk update, reindex, and conformance.
3. Deploy to non-production with `Ignixa`.
4. Verify fallback guard reports no unapproved Firely fallback.
5. Run E2E mode matrix.
6. Promote only when the P0 matrix is green.

## Rollback

Set SDK mode to `Firely` and redeploy. Firely mode must not require active Ignixa providers.

## Troubleshooting

Use fallback guard logs to identify surface and reason. Any Firely fallback in Ignixa mode is a merge-blocking defect unless listed in the shim register.
```

- [ ] **Step 3: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core.UnitTests\Features\Sdk\SdkFallbackGuardTests.cs docs\ignixa\sdk-mode-rollout.md docs\ignixa\shim-register.md
git commit -m "Document SDK mode rollout and fallback diagnostics" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 13: Full verification gate

**Files:**
- No new files.

- [ ] **Step 1: Run unit test projects touched by the plan**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net9.0 --no-restore
dotnet test .\src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --framework net9.0 --no-restore
dotnet test .\src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --framework net9.0 --no-restore
```

Expected: PASS.

- [ ] **Step 2: Run build for E2E projects touched by the plan**

Run:

```powershell
dotnet build .\test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --no-restore
dotnet build .\test\Microsoft.Health.Fhir.Stu3.Tests.E2E\Microsoft.Health.Fhir.Stu3.Tests.E2E.csproj --no-restore
dotnet build .\test\Microsoft.Health.Fhir.R4B.Tests.E2E\Microsoft.Health.Fhir.R4B.Tests.E2E.csproj --no-restore
dotnet build .\test\Microsoft.Health.Fhir.R5.Tests.E2E\Microsoft.Health.Fhir.R5.Tests.E2E.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 3: Push and monitor CI**

Run:

```powershell
git push origin personal/bkowitz/ignixa-sdk-next-steps
gh pr checks 5467 --repo microsoft/fhir-server
```

Expected: existing PR checks remain green on the feature branch if this work is later merged into `feature/ignixa-sdk`, and the personal branch is ready for review as an implementation branch or stacked PR.

## Self-Review Checklist

- Spec coverage: SDK-1 through DOC-1 have implementation tasks. TENANT-1 remains P2 and is intentionally excluded from implementation until product scope changes.
- Placeholder scan: no plan steps use unfilled markers.
- Type consistency: `FhirSdkMode`, `SdkConfiguration`, `ISdkModeProvider`, `SdkModeProvider`, `ISdkFallbackGuard`, and `SdkFallbackGuard` are introduced before use.
- Risk callout: PATCH and conformance validation may expose Ignixa SDK capability gaps. Those gaps are P0 blockers under the approved scope.
