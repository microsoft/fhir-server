# PR 1 — MediatR → Medino + Shared 11.x Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace MediatR 12.5.0 with the MIT-licensed Medino 3.0.3 and move the Microsoft.Health shared package line to 11.x across the FHIR Server repository, with zero behavioral change. Target frameworks stay `net9.0;net8.0`, while the build SDK moves to .NET 10 because the shared 11.x SQL build task is `net10.0`.

**Architecture:** Compiler-driven big-bang. Swap the NuGet package, swap the `using` namespace, then let the compiler flag every call/handler/method site that needs a deterministic rename (`Send`→`SendAsync`, `Publish`→`PublishAsync`, `Handle`→`HandleAsync`, `Execute`→`ExecuteAsync`, `next(ct)`→`next()`). Medino has no `IRequestPreProcessor`, so the three FHIR validators are re-expressed as `IPipelineBehavior<TRequest, TResponse>` that run their check then call `await next()`. Medino runs exception actions natively, so the two explicit exception-processor behavior registrations are deleted, not migrated.

**Tech Stack:** C# / .NET SDK 10.0.301 targeting `net9.0;net8.0`, Medino 3.0.3 + Medino.Extensions.DependencyInjection 3.0.3, Microsoft.Health shared packages 11.0.111, FluentValidation, Microsoft.Testing.Platform (MTP) xUnit, Central Package Management (`Directory.Packages.props`).

**Source of truth:** `docs/superpowers/specs/2026-06-15-dotnet10-upgrade-design.md` §4. ADO Task #195647.

---

## Medino API reference (confirmed from source @ `9abae7ac` + MIGRATION.md)

Read this once before starting; every task below depends on it.

- Single namespace `Medino` for all core types. `AddMedino` lives in `Medino.Extensions.DependencyInjection`.
- **`IMediator`** is unified (replaces MediatR's `ISender`/`IPublisher`/`IMediator`). Three methods, each with an optional `CancellationToken = default`:
  - `Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : ICommand;`
  - `Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);`
  - `Task PublishAsync<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;`
- Marker interfaces: `IRequest<out TResponse>` (**generic only — there is NO non-generic `IRequest`**), `ICommand` (void), `INotification`. There is **no `Unit` type** and **no `IBaseRequest`**.
- **`IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull`** — single method:
  `Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);`
- **`public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();`** — **parameterless**. Every existing `next(cancellationToken)` call MUST become `next()`.
- **`IRequestExceptionAction<in TRequest, in TException> where TRequest : notnull where TException : Exception`** — single method:
  `Task ExecuteAsync(TRequest request, TException exception, CancellationToken cancellationToken);`
- Handler / notification-handler method name is `HandleAsync` (was `Handle`).
- **No pre/post-processors.** MIGRATION.md: "Pre/Post Processors → Use Pipeline Behaviors."
- **`AddMedino`** scans the supplied assemblies and auto-registers handlers, notification handlers, **closed** `IPipelineBehavior` implementations, and **closed** `IRequestExceptionAction` implementations. **Open-generic implementations are skipped** and must be registered manually. Exception actions run natively inside `SendAsync` — no behavior wrapper is registered for them.

---

## File map

| File | Change |
|------|--------|
| `Directory.Packages.props` | Remove MediatR; add Medino + Medino.Extensions.DependencyInjection 3.0.3; bump Microsoft.Health shared packages to 11.0.111 and aligned transitive pins |
| `global.json` | Use .NET SDK 10.0.301 so `Microsoft.Health.Tools.Sql.Tasks` 11.x can load its `net10.0` MSBuild task while still targeting `net9.0;net8.0` |
| ADO build templates | Install .NET 8.0.28 and/or 9.0.17 runtimes before SDK 10 because `Microsoft.Health.Extensions.BuildTimeCodeGenerator` 11.x invokes target-specific `tools/net8.0` and `tools/net9.0` generators |
| `build/docker/Dockerfile` | Use the .NET 10 SDK build image for SQL script generation; runtime image and publish TFM remain net9 until PR 2 |
| ~230 `*.cs` with `using MediatR…` | Namespace swap to `using Medino;` (scripted) |
| `src/Microsoft.Health.Fhir.Core/Messages/Bundle/BundleRequest.cs` | Remove redundant non-generic `, IRequest` |
| `src/Microsoft.Health.Fhir.Core/Messages/Create/CreateResourceRequest.cs` | Remove redundant non-generic `, IRequest` |
| `src/Microsoft.Health.Fhir.Core/Messages/Upsert/UpsertResourceRequest.cs` | Remove redundant non-generic `, IRequest` |
| `src/Microsoft.Health.Fhir.Core/Messages/Operation/ValidateOperationRequest.cs` | Remove redundant non-generic `, IRequest` |
| `src/Microsoft.Health.Fhir.Core/Messages/MemberMatch/MemberMatchRequest.cs` | Remove redundant non-generic `, IRequest` |
| `src/Microsoft.Health.Fhir.Shared.Api/Modules/MediationModule.cs` | Full rewrite: AddMedino, drop exception behaviors, manual pre-validator regs |
| `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateRequestPreProcessor.cs` | Convert to `IPipelineBehavior<TRequest, TResponse>` |
| `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateBundlePreProcessor.cs` | Convert to `IPipelineBehavior<BundleRequest, BundleResponse>` |
| `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateCapabilityPreProcessor.cs` | Convert to `IPipelineBehavior<TRequest, TResponse>` |
| `src/Microsoft.Health.Fhir.Shared.Core/Features/Search/Behaviors/ListSearchPipeBehavior.cs` | `Handle`→`HandleAsync`, `next(ct)`→`next()` |
| `src/Microsoft.Health.Fhir.Core/Features/Search/Parameters/CreateOrUpdateSearchParameterBehavior.cs` | `Handle`→`HandleAsync`, `next(ct)`→`next()` |
| `src/Microsoft.Health.Fhir.Core/Features/Search/Parameters/DeleteSearchParameterBehavior.cs` | `Handle`→`HandleAsync`, `next(ct)`→`next()` |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlExceptionActionProcessor.cs` | `Execute`→`ExecuteAsync`, add `where TRequest : notnull` |
| `src/Microsoft.Health.Fhir.SchemaManager/SchemaManagerServiceCollectionBuilder.cs` | `AddMediatR`→`AddMedino` |
| ~63 handlers + test call sites | `Handle`→`HandleAsync` (build-driven) |
| ~180 mediator call sites | `Send`→`SendAsync`, `Publish`→`PublishAsync` (scripted + build-verified) |

> **Note on §4.3 of the spec:** the spec names `BeforePipelineBehavior<TRequest, TResponse>`. This plan realizes that same design with the plain, source-confirmed `IPipelineBehavior<TRequest, TResponse>` interface (run the check, then `return await next()`). Semantics are identical — throwing before `next()` short-circuits the handler exactly as the old `Process(...)` did — and this avoids depending on a base class whose exact signature could not be confirmed from source.

---

## Task 0: Baseline — confirm green before touching anything

**Files:** none (read-only).

- [ ] **Step 1: Build the solution on both target frameworks**

Run:
```powershell
cd C:\src\copilot-worktrees\fhir-server\mikaelweave-refactored-chainsaw
dotnet build Microsoft.Health.Fhir.sln -c Release
```
Expected: `Build succeeded.` with 0 errors. (This repo sets `TreatWarningsAsErrors=true` in `Directory.Build.props`, so a clean build also means 0 warnings.)

- [ ] **Step 2: Record the MediatR footprint (the migration's burn-down target)**

Run:
```powershell
(rg -l "using MediatR" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**" | Measure-Object).Count
rg -c "\.Send\(|\.Publish\(" --glob "*.cs" -g "!**/obj/**" | Measure-Object | Select-Object -ExpandProperty Count
```
Expected: a non-zero file count (~230). Note the number; the final task asserts `rg "MediatR"` returns zero.

- [ ] **Step 3: Run the unit suite to capture a green baseline**

Run:
```powershell
dotnet test src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -c Release -f net9.0 -- --report-trx
```
Expected: all tests pass. (If pre-existing failures exist, note them so they aren't blamed on the migration.)

---

## Task 1: Swap the NuGet package

**Files:**
- Modify: `Directory.Packages.props:64`

- [ ] **Step 1: Remove the MediatR package version**

Delete this line (currently line 64):
```xml
    <PackageVersion Include="MediatR" Version="12.5.0" />
```

- [ ] **Step 2: Add the two Medino package versions in its place**

Insert (alphabetical order is not enforced in this file, but keep the two together). `3.0.3` is the latest stable on nuget.org (verified via `dotnet package search Medino`; owner `brendankowitz`) and is routed to the public `nuget.org` feed by the `*` `packageSourceMapping` rule in `nuget.config`:
```xml
    <PackageVersion Include="Medino" Version="3.0.3" />
    <PackageVersion Include="Medino.Extensions.DependencyInjection" Version="3.0.3" />
```

- [ ] **Step 3: Update the `<PackageReference>` entries**

`MediatR` is referenced (without a version, per CPM) in the project files that use it. Find them and repoint to Medino:
```powershell
rg -l "PackageReference Include=\"MediatR\"" --glob "*.csproj"
```
For each hit, replace the single MediatR reference with the two Medino references. Example (`Microsoft.Health.Fhir.Core.csproj`):
```xml
<!-- before -->
<PackageReference Include="MediatR" />
<!-- after -->
<PackageReference Include="Medino" />
<PackageReference Include="Medino.Extensions.DependencyInjection" />
```

- [ ] **Step 4: Restore to verify the package resolves**

Run:
```powershell
dotnet restore Microsoft.Health.Fhir.sln
```
Expected: restore succeeds (no NU1101 "unable to find package Medino"). Do **not** build yet — the source still says `using MediatR;`.

---

## Task 2: Namespace swap (`using MediatR…` → `using Medino;`)

**Files:** every `*.cs` containing a MediatR using directive (~230). Scripted.

> The eight files that get a full or structural rewrite later (MediationModule, the three validators, SqlExceptionActionProcessor, and the SchemaManager builder) are also touched by this script; their later tasks overwrite them with authoritative content, so the transient state is harmless. `MediationModule.cs` is the only file that has **both** `using MediatR;` and `using MediatR.Pipeline;`; the script below would leave it with a duplicate `using Medino;` (a CS0105 warning → build error under `TreatWarningsAsErrors`), but Task 4 replaces that file wholesale, so it never reaches a build.

- [ ] **Step 1: Run the namespace-swap script**

Run:
```powershell
cd C:\src\copilot-worktrees\fhir-server\mikaelweave-refactored-chainsaw
$files = rg -l "using MediatR" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**"
foreach ($f in $files) {
    $text = Get-Content -Raw -LiteralPath $f
    $text = $text -replace "using MediatR\.Pipeline;", "using Medino;"
    $text = $text -replace "using MediatR;", "using Medino;"
    Set-Content -NoNewline -LiteralPath $f -Value $text
}
```

- [ ] **Step 2: Verify no `using MediatR` remains**

Run:
```powershell
rg -n "using MediatR" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**"
```
Expected: no output.

---

## Task 3: Remove the redundant non-generic `IRequest` marker

**Files (each already implements `IRequest<TResponse>`; the bare `, IRequest` is MediatR's `IBaseRequest`, which Medino does not have):**
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Bundle/BundleRequest.cs:15`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Create/CreateResourceRequest.cs:16`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Upsert/UpsertResourceRequest.cs:16`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Operation/ValidateOperationRequest.cs:13`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/MemberMatch/MemberMatchRequest.cs:11`

- [ ] **Step 1: Confirm the only standalone `IRequest` tokens are these five declarations**

Run:
```powershell
rg --pcre2 -n "\bIRequest\b(?!<)" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**"
```
Expected: exactly five hits, all base-type lists on the classes above. (No `where T : IRequest` constraints, no `is IRequest` casts — so removing the marker is safe.)

- [ ] **Step 2: Edit each declaration — drop `, IRequest`**

```csharp
// BundleRequest.cs:15  — before
public class BundleRequest : IRequest<BundleResponse>, IRequest, IRequireCapability
// after
public class BundleRequest : IRequest<BundleResponse>, IRequireCapability
```
```csharp
// CreateResourceRequest.cs:16  — before
public class CreateResourceRequest : BaseBundleInnerRequest, IRequest<UpsertResourceResponse>, IRequest, IRequireCapability
// after
public class CreateResourceRequest : BaseBundleInnerRequest, IRequest<UpsertResourceResponse>, IRequireCapability
```
```csharp
// UpsertResourceRequest.cs:16  — before
public class UpsertResourceRequest : BaseBundleInnerRequest, IRequest<UpsertResourceResponse>, IRequest, IRequireCapability
// after
public class UpsertResourceRequest : BaseBundleInnerRequest, IRequest<UpsertResourceResponse>, IRequireCapability
```
```csharp
// ValidateOperationRequest.cs:13  — before
public class ValidateOperationRequest : IRequest<ValidateOperationResponse>, IRequest
// after
public class ValidateOperationRequest : IRequest<ValidateOperationResponse>
```
```csharp
// MemberMatchRequest.cs:11  — before
public sealed class MemberMatchRequest : IRequest<MemberMatchResponse>, IRequest
// after
public sealed class MemberMatchRequest : IRequest<MemberMatchResponse>
```

- [ ] **Step 3: Re-verify**

Run:
```powershell
rg --pcre2 -n "\bIRequest\b(?!<)" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**"
```
Expected: no output.

---

## Task 4: Rewrite `MediationModule.cs`

**Files:**
- Modify (full file): `src/Microsoft.Health.Fhir.Shared.Api/Modules/MediationModule.cs`

- [ ] **Step 1: Replace the entire file with the content below**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Medino;
using Medino.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Installs mediation components in container
    /// </summary>
    public class MediationModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddMedino(KnownAssemblies.All);

            // Medino has no IRequestPreProcessor. The two closed validation behaviors
            // (ValidateBundlePreProcessor) are auto-registered by AddMedino's assembly scan.
            // The open-generic validation behaviors are skipped by the scan and must be
            // registered manually as open-generic IPipelineBehavior<,>.
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateRequestPreProcessor<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateCapabilityPreProcessor<,>));

            // Allows handlers to provide capabilities
            var openRequestInterfaces = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(INotificationHandler<>),
            };

            services.TypesInSameAssembly(KnownAssemblies.All)
                .Where(y => y.Type.IsGenericType && openRequestInterfaces.Contains(y.Type.GetGenericTypeDefinition()))
                .Transient()
                .AsImplementedInterfaces(x => x == typeof(IProvideCapability));
        }
    }
}
```

What changed vs. the original:
- `AddMediatR(cfg => …)` → `services.AddMedino(KnownAssemblies.All)`.
- The two `cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestException*ProcessorBehavior<,>))` lines are **deleted** — Medino runs `IRequestExceptionAction` natively.
- The three `cfg.AddRequestPreProcessor(...)` lines become two manual open-generic registrations (`ValidateRequestPreProcessor<,>`, `ValidateCapabilityPreProcessor<,>`). `ValidateBundlePreProcessor` is closed, so AddMedino's scan registers it — omit it here to avoid a double registration.
- Removed now-unused usings (`System`, `Microsoft.Health.Fhir.Core.Features.Conformance`, `Microsoft.Health.Fhir.Core.Messages.Bundle`). The `IProvideCapability` scan block is unchanged.

---

## Task 5: Convert `ValidateRequestPreProcessor` to a pipeline behavior

**Files:**
- Modify (full file): `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateRequestPreProcessor.cs`

- [ ] **Step 1: Replace the entire file with the content below**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation;
using Medino;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateRequestPreProcessor<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : class
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidateRequestPreProcessor(IEnumerable<IValidator<TRequest>> validators)
        {
            EnsureArg.IsNotNull(validators, nameof(validators));

            _validators = validators;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var allResults = (await Task.WhenAll(_validators.Select(x => x.ValidateAsync(request, cancellationToken)))).Where(x => x != null).ToArray();

            if (!allResults.All(x => x.IsValid))
            {
                throw new ResourceNotValidException(allResults.SelectMany(x => x.Errors).ToList());
            }

            return await next();
        }
    }
}
```

Notes: gains a `TResponse` type parameter (now `<TRequest, TResponse>`); `where TRequest : class` already satisfies Medino's `notnull` constraint; the validation body is unchanged; on success it calls `await next()` to continue the pipeline; on failure it throws before `next()`, exactly as the old `Process` did.

---

## Task 6: Convert `ValidateBundlePreProcessor` to a pipeline behavior

**Files:**
- Modify (full file): `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateBundlePreProcessor.cs`

- [ ] **Step 1: Replace the entire file with the content below**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Medino;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateBundlePreProcessor : IPipelineBehavior<BundleRequest, BundleResponse>
    {
        public Task<BundleResponse> HandleAsync(BundleRequest request, RequestHandlerDelegate<BundleResponse> next, CancellationToken cancellationToken)
        {
            if (request.Bundle.InstanceType != KnownResourceTypes.Bundle)
            {
                throw new RequestNotValidException(Core.Resources.BundleRequiredForBatchOrTransaction);
            }

            return next();
        }
    }
}
```

Notes: closed `IPipelineBehavior<BundleRequest, BundleResponse>` → auto-registered by `AddMedino` (do not register it manually). The validation throw is unchanged; the old `return Task.CompletedTask` becomes `return next()` so the bundle handler still runs on the valid path. The `using Task = System.Threading.Tasks.Task;` alias is retained because `BundleRequest`'s namespace pulls in a conflicting `Task` symbol.

---

## Task 7: Convert `ValidateCapabilityPreProcessor` to a pipeline behavior

**Files:**
- Modify (full file): `src/Microsoft.Health.Fhir.Core/Features/Validation/ValidateCapabilityPreProcessor.cs`

- [ ] **Step 1: Replace the entire file with the content below**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateCapabilityPreProcessor<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateCapabilityPreProcessor(IConformanceProvider conformanceProvider)
        {
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));

            _conformanceProvider = conformanceProvider;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is IRequireCapability provider)
            {
                if (!await _conformanceProvider.SatisfiesAsync(provider.RequiredCapabilities().ToList(), cancellationToken))
                {
                    throw new MethodNotAllowedException(Core.Resources.RequestedActionNotAllowed);
                }
            }

            return await next();
        }
    }
}
```

Notes: gains a `TResponse` type parameter and an explicit `where TRequest : notnull` (required by `IPipelineBehavior`; the original had no constraint). Body unchanged; calls `await next()` after the capability check.

---

## Task 8: Migrate `ListSearchPipeBehavior`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Search/Behaviors/ListSearchPipeBehavior.cs`

(`using MediatR;` → `using Medino;` was already applied by Task 2.)

- [ ] **Step 1: Rename the method (line 53)**

```csharp
// before
public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, RequestHandlerDelegate<SearchResourceResponse> next, CancellationToken cancellationToken)
// after
public async Task<SearchResourceResponse> HandleAsync(SearchResourceRequest request, RequestHandlerDelegate<SearchResourceResponse> next, CancellationToken cancellationToken)
```

- [ ] **Step 2: Drop the argument from both `next(...)` calls (lines 63 and 95)**

Both occurrences:
```csharp
// before
return await next(cancellationToken);
// after
return await next();
```

---

## Task 9: Migrate `CreateOrUpdateSearchParameterBehavior`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/Parameters/CreateOrUpdateSearchParameterBehavior.cs`

(`using MediatR;` → `using Medino;` already applied by Task 2.)

- [ ] **Step 1: Rename both method overloads (lines 56 and 73)**

```csharp
// line 56 — before
public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
// after
public async Task<UpsertResourceResponse> HandleAsync(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
```
```csharp
// line 73 — before
public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
// after
public async Task<UpsertResourceResponse> HandleAsync(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
```

- [ ] **Step 2: Drop the argument from all four `next(...)` calls (lines 66, 70, 115, 119)**

Every occurrence:
```csharp
// before
return await next(cancellationToken);
// after
return await next();
```

---

## Task 10: Migrate `DeleteSearchParameterBehavior`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/Parameters/DeleteSearchParameterBehavior.cs`

(`using MediatR;` → `using Medino;` already applied by Task 2. The constraint `where TDeleteResourceRequest : DeleteResourceRequest, IRequest<TDeleteResourceResponse>` now resolves `IRequest<>` from Medino — no edit needed; `DeleteResourceRequest` is a class, so the implicit `notnull` is satisfied.)

- [ ] **Step 1: Rename the method (line 60)**

```csharp
// before
public async Task<TDeleteResourceResponse> Handle(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
// after
public async Task<TDeleteResourceResponse> HandleAsync(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
```

- [ ] **Step 2: Drop the argument from both `next(...)` calls (lines 93 and 96)**

```csharp
// before
return await next(cancellationToken);
// after
return await next();
```

---

## Task 11: Migrate `SqlExceptionActionProcessor`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlExceptionActionProcessor.cs`

(`using MediatR.Pipeline;` → `using Medino;` already applied by Task 2.)

- [ ] **Step 1: Add the `notnull` constraint (line 21-22)**

```csharp
// before
public class SqlExceptionActionProcessor<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TException : Exception
// after
public class SqlExceptionActionProcessor<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull
    where TException : Exception
```

- [ ] **Step 2: Rename the method (line 32)**

```csharp
// before
public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
// after
public Task ExecuteAsync(TRequest request, TException exception, CancellationToken cancellationToken)
```

(Its registration in `FhirServerBuilderSqlServerRegistrationExtensions.cs` registers the closed `IRequestExceptionAction<,>` and is unaffected — leave it as-is.)

---

## Task 12: Migrate `SchemaManagerServiceCollectionBuilder`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SchemaManager/SchemaManagerServiceCollectionBuilder.cs`

- [ ] **Step 1: Add the Medino DI using (after line 13)**

Insert with the other usings:
```csharp
using Medino.Extensions.DependencyInjection;
```

- [ ] **Step 2: Swap the registration call (line 52)**

```csharp
// before
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SchemaUpgradedNotification).Assembly));
// after
services.AddMedino(typeof(SchemaUpgradedNotification).Assembly);
```

---

## Task 13: Build-and-fix loop — handler methods and mediator calls

This is where the compiler drives the remaining ~63 handler renames and ~180 call renames.

**Files:** handler classes implementing `IRequestHandler<,>` / `INotificationHandler<>`, and any caller of `IMediator`.

- [ ] **Step 1: Scripted pass for mediator call sites**

Run (scoped to mediator-typed variables to avoid touching unrelated `.Send(`/`.Publish(`):
```powershell
cd C:\src\copilot-worktrees\fhir-server\mikaelweave-refactored-chainsaw
$files = rg --pcre2 -l "\b_?[Mm]ediator\b\??\.(Send|Publish)\(" --glob "*.cs" -g "!**/obj/**" -g "!**/bin/**"
foreach ($f in $files) {
    $text = Get-Content -Raw -LiteralPath $f
    $text = [System.Text.RegularExpressions.Regex]::Replace($text, "(\b_?[Mm]ediator\b\??)\.Send\(", '$1.SendAsync(')
    $text = [System.Text.RegularExpressions.Regex]::Replace($text, "(\b_?[Mm]ediator\b\??)\.Publish\(", '$1.PublishAsync(')
    Set-Content -NoNewline -LiteralPath $f -Value $text
}
```

- [ ] **Step 2: Build to surface every remaining rename**

Run:
```powershell
dotnet build Microsoft.Health.Fhir.sln -c Release 2>&1 | Select-String -Pattern "error" | Select-Object -First 60
```
Expected error categories (each is a deterministic fix):
- `'IMediator' does not contain a definition for 'Send'` / `'Publish'` → a call the script missed (e.g. a mediator passed as a differently-named parameter). Rename to `SendAsync` / `PublishAsync`.
- `'XHandler' does not implement interface member 'IRequestHandler<TRequest,TResponse>.HandleAsync(...)'` → rename that class's `Handle` method to `HandleAsync`.
- `'XNotificationHandler' does not implement interface member 'INotificationHandler<TNotification>.HandleAsync(...)'` → rename `Handle` to `HandleAsync`.

- [ ] **Step 3: Rename handler methods**

For each handler flagged in Step 2, rename the override:
```csharp
// before
public async Task<SomeResponse> Handle(SomeRequest request, CancellationToken cancellationToken)
// after
public async Task<SomeResponse> HandleAsync(SomeRequest request, CancellationToken cancellationToken)
```
```csharp
// notification handler — before
public Task Handle(SomeNotification notification, CancellationToken cancellationToken)
// after
public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
```

- [ ] **Step 4: Re-build until the main source compiles**

Run:
```powershell
dotnet build Microsoft.Health.Fhir.sln -c Release 2>&1 | Select-String -Pattern ": error" | Select-Object -First 60
```
Repeat Steps 2-3 until production projects build clean. (Test projects may still show errors — handled in Task 14.) If a `RequestExceptionActionProcessorBehavior` / `RequestExceptionProcessorBehavior` "type or namespace not found" error appears, it means a stray reference to the deleted MediatR exception behaviors survived — delete that reference; Medino needs no replacement.

---

## Task 14: Fix test call sites

**Files:** unit/integration test projects that invoke handlers or exception actions directly.

- [ ] **Step 1: Build the test projects to surface call-site errors**

Run:
```powershell
dotnet build Microsoft.Health.Fhir.sln -c Release 2>&1 | Select-String -Pattern ": error" | Select-Object -First 80
```
Expected: `'XHandler' does not contain a definition for 'Handle'` (tests calling `handler.Handle(...)`), and for SQL tests `'SqlExceptionActionProcessor<,>' does not contain a definition for 'Execute'`.

- [ ] **Step 2: Rename direct handler invocations in tests**

For each flagged call:
```csharp
// before
var response = await _fixture.GetResourceHandler.Handle(request, CancellationToken.None);
// after
var response = await _fixture.GetResourceHandler.HandleAsync(request, CancellationToken.None);
```
Do **not** blind-replace `.Handle(` repo-wide — `.Handle(` and `.Execute(` also appear on unrelated objects (job handlers, command objects). Fix only the sites the compiler flags.

- [ ] **Step 3: Rename exception-action invocations in SQL tests**

```csharp
// before
await processor.Execute(request, exception, CancellationToken.None);
// after
await processor.ExecuteAsync(request, exception, CancellationToken.None);
```

- [ ] **Step 4: Build the full solution clean**

Run:
```powershell
dotnet build Microsoft.Health.Fhir.sln -c Release
```
Expected: `Build succeeded.` 0 errors, 0 warnings (warnings are errors here).

- [ ] **Step 5: Commit the compiling migration**

```powershell
git add -A
git commit -m "Migrate from MediatR to Medino

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 15: Verify behavior and finalize

- [ ] **Step 1: Assert zero MediatR references remain**

Run:
```powershell
rg -n "MediatR" --glob "*.cs" --glob "*.csproj" --glob "*.props" -g "!**/obj/**" -g "!**/bin/**"
```
Expected: no output. (Acceptance criterion §4.6.)

- [ ] **Step 2: Run the validation tests (pre-processor behavior must be preserved)**

Run:
```powershell
dotnet test src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj -c Release -f net9.0 --no-build -- --filter "FullyQualifiedName~Validation" --report-trx
```
Expected: all validation tests pass — confirms `ResourceNotValidException` / `RequestNotValidException` / `MethodNotAllowedException` still short-circuit as before.

- [ ] **Step 3: Run the full unit suite on net9.0**

Run:
```powershell
dotnet test Microsoft.Health.Fhir.sln -c Release --no-build -f net9.0 -- --retry-failed-tests 3 --report-trx
```
Expected: all tests pass.

- [ ] **Step 4: Run the full unit suite on net8.0 (the downlevel floor)**

Run:
```powershell
dotnet test Microsoft.Health.Fhir.sln -c Release --no-build -f net8.0 -- --retry-failed-tests 3 --report-trx
```
Expected: all tests pass. (Acceptance criterion §4.6: green on both `net9.0` and `net8.0`.)

- [ ] **Step 5: Final commit (if Steps 1-4 required any fixups)**

```powershell
git add -A
git commit -m "Fix tests after MediatR to Medino migration

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Acceptance criteria (spec §4.6)

- [ ] Solution builds clean on `net9.0;net8.0` with `TreatWarningsAsErrors=true` and **no remaining `MediatR` references** (`rg MediatR` → empty).
- [ ] `global.json` selects the Microsoft.Testing.Platform test runner, and ADO test tasks publish MTP-generated TRX files explicitly instead of using DotNetCoreCLI's VSTest logger injection.
- [ ] All unit + integration tests green on both frameworks.
- [ ] Validation pre-processor behavior preserved (validation test suite green).
