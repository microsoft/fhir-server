# Separate Kubernetes Health Probes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the FHIR server's single `/health/check` endpoint into four probe-specific endpoints (`/health/check`, `/health/startup`, `/health/ready`, `/health/live`) so Kubernetes stops routing traffic to pods before storage is initialized, without crash-looping pods on persistent (e.g. CMK) failures.

**Architecture:** Health checks declare probe membership via tags (`probe:startup`, `probe:readiness`, plus the shared package's existing `datastore:sqlServer`). Four routes are mapped in `FhirServerApplicationBuilderExtensions.UseFhirServer`, each filtered by a tag predicate with an explicit HTTP status map. `StorageInitializedHealthCheck` becomes a pure Healthy/Unhealthy startup gate (no CMK/Key Vault call). No `healthcare-shared-components` change is required.

**Tech Stack:** .NET / ASP.NET Core health checks (`Microsoft.Extensions.Diagnostics.HealthChecks`), MediatR notifications, xUnit + NSubstitute, `Microsoft.Extensions.Time.Testing.FakeTimeProvider` + the repo's `Clock`/`ClockResolver` abstraction.

**Source spec:** `docs/superpowers/specs/2026-07-13-fhir-separate-health-probes-design.md`

---

## File Structure

**New files**
- `src/Microsoft.Health.Fhir.Api/Features/Health/HealthCheckTags.cs` — tag string constants.
- `src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheckState.cs` — immutable state record for the behavior check.
- `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckRegistrationTests.cs` — registration/assertion tests.
- `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckEndpointTests.cs` — endpoint routing/status tests (TestServer).

**Modified files**
- `src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheckConfiguration.cs` — remove `StartupDegradedDelay`.
- `src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheck.cs` — pure gate; drop CMK + constructor validation; `volatile`.
- `src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheck.cs` — thread-safe immutable-state swap.
- `src/Microsoft.Health.Fhir.Core/Features/Routing/KnownRoutes.cs` — 3 new route constants.
- `src/Microsoft.Health.Fhir.Api/Registration/FhirServerApplicationBuilderExtensions.cs` — 4 routes, shared response writer, startup assertion.
- `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` — add tags to Behavior + Storage checks.
- `src/Microsoft.Health.Fhir.CosmosDb/Registration/FhirServerBuilderCosmosDbRegistrationExtensions.cs` — tag Cosmos data-store check.
- `src/Microsoft.Health.Fhir.Shared.Web/Startup.cs` — bind config with `.Validate().ValidateOnStart()`.
- `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Features/Health/StorageInitializedHealthCheckTests.cs` — rewrite for pure gate.
- `src/Microsoft.Health.Fhir.Api.UnitTests/Features/Health/ImproperBehaviorHealthCheckTests.cs` — add concurrency test.

---

## Task 1: Tag constants

**Files:**
- Create: `src/Microsoft.Health.Fhir.Api/Features/Health/HealthCheckTags.cs`

- [ ] **Step 1: Create the constants file**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Health-check tag constants used to select which checks each Kubernetes probe runs.
    /// A tag typo silently fails open (an empty selection resolves to Healthy => HTTP 200),
    /// so every registration and predicate MUST reference these constants rather than literals.
    /// </summary>
    public static class HealthCheckTags
    {
        /// <summary>Tag for the startup gate check (<see cref="StorageInitializedHealthCheck"/>).</summary>
        public const string ProbeStartup = "probe:startup";

        /// <summary>Tag for checks that participate in the readiness/routing decision.</summary>
        public const string ProbeReadiness = "probe:readiness";

        /// <summary>
        /// Mirrors the tag applied by the healthcare-shared-components SQL registration.
        /// A startup assertion fails loudly if the shared value ever drifts from this literal.
        /// </summary>
        public const string DataStoreSqlServer = "datastore:sqlServer";
    }
}
```

- [ ] **Step 2: Build the Api project**

Run: `dotnet build src/Microsoft.Health.Fhir.Api/Microsoft.Health.Fhir.Api.csproj -clp:ErrorsOnly`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Api/Features/Health/HealthCheckTags.cs
git commit -m "feat(health): add HealthCheckTags constants"
```

---

## Task 2: Route constants

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Routing/KnownRoutes.cs:80`

- [ ] **Step 1: Add the three route constants**

Locate this line (currently line 80):

```csharp
        public const string HealthCheck = "/health/check";
```

Replace it with:

```csharp
        public const string HealthCheck = "/health/check";
        public const string HealthCheckStartup = "/health/startup";
        public const string HealthCheckReady = "/health/ready";
        public const string HealthCheckLive = "/health/live";
```

- [ ] **Step 2: Build the Core project**

Run: `dotnet build src/Microsoft.Health.Fhir.Core/Microsoft.Health.Fhir.Core.csproj -clp:ErrorsOnly`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Core/Features/Routing/KnownRoutes.cs
git commit -m "feat(health): add startup/ready/live route constants"
```

---

## Task 3: Simplify the configuration

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheckConfiguration.cs`

- [ ] **Step 1: Replace the file contents**

The `StartupDegradedDelay` knob is removed (the gate no longer has a Degraded tier). Replace the whole file with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Configures the startup health-check gate.
    /// </summary>
    public class StorageInitializedHealthCheckConfiguration
    {
        /// <summary>
        /// The configuration section name.
        /// </summary>
        public const string SectionName = "HealthChecks:StorageInitialization";

        /// <summary>
        /// Gets or sets the absolute backstop after which the startup gate hands off to
        /// readiness (returns Healthy) regardless of initialization state. Must satisfy the
        /// invariant: legit-init-p99 &lt; StorageInitializationTimeout &lt; k8s-startup-budget.
        /// </summary>
        public TimeSpan StorageInitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
```

- [ ] **Step 2: Do not build yet**

This intentionally breaks `StorageInitializedHealthCheck` (still references `StartupDegradedDelay`) and its tests. Those are fixed in Task 4. Proceed directly.

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheckConfiguration.cs
git commit -m "refactor(health): remove StartupDegradedDelay from storage-init config"
```

---

## Task 4: Rewrite `StorageInitializedHealthCheck` as a pure gate (TDD)

**Files:**
- Test: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Features/Health/StorageInitializedHealthCheckTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheck.cs`

- [ ] **Step 1: Rewrite the failing tests**

Replace the entire contents of `StorageInitializedHealthCheckTests.cs` with the pure-gate tests below. The check no longer takes `IDatabaseStatusReporter`, no longer validates in its constructor, and never returns `Degraded`.

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.Features.Health;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.DataSourceValidation)]
public class StorageInitializedHealthCheckTests
{
    [Fact]
    public async Task GivenStorageInitialized_WhenCheckHealthAsync_ThenReturnsHealthy()
    {
        StorageInitializedHealthCheck sut = CreateSut();

        await sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenNotInitialized_WhenCheckHealthAsyncBeforeTimeout_ThenReturnsUnhealthy()
    {
        StorageInitializedHealthCheck sut = CreateSut();

        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage is initializing", result.Description);
    }

    [Fact]
    public async Task GivenNotInitializedAndTimeoutElapsed_WhenCheckHealthAsync_ThenReturnsHealthyBackstop()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(1));
            timeProvider.Advance(TimeSpan.FromMilliseconds(2));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }

    [Fact]
    public async Task GivenNotInitializedAtExactBoundary_WhenCheckHealthAsync_ThenReturnsHealthyBackstop()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(2));
            timeProvider.Advance(TimeSpan.FromMilliseconds(2));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }

    [Fact]
    public async Task GivenConcurrentNotificationAndProbes_WhenCheckHealthAsync_ThenHandoffIsObserved()
    {
        StorageInitializedHealthCheck sut = CreateSut();
        using var cts = new CancellationTokenSource();

        Task reader = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            }
        });

        await sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);
        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        cts.Cancel();
        await reader;

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void GivenDefaultConfiguration_ThenStorageInitializationTimeoutMatchesDocumentedInvariant()
    {
        // Guards the K8s-budget invariant: the documented app timeout is 5 minutes.
        // If this default changes, the fhir-paas startup budget must be re-checked (must stay strictly greater).
        Assert.Equal(TimeSpan.FromMinutes(5), new StorageInitializedHealthCheckConfiguration().StorageInitializationTimeout);
    }

    private static StorageInitializedHealthCheck CreateSut(TimeSpan? storageInitializationTimeout = null)
    {
        return new StorageInitializedHealthCheck(
            Options.Create(
                new StorageInitializedHealthCheckConfiguration
                {
                    StorageInitializationTimeout = storageInitializationTimeout ?? TimeSpan.FromMinutes(5),
                }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile / fail**

Run: `dotnet test src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj --filter FullyQualifiedName~StorageInitializedHealthCheckTests`
Expected: Build FAIL — `StorageInitializedHealthCheck` constructor still requires `IDatabaseStatusReporter` and references removed members.

- [ ] **Step 3: Rewrite the health check as a pure gate**

Replace the entire contents of `StorageInitializedHealthCheck.cs` with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Pure startup gate. Returns Healthy once storage initialization completes or the configured
    /// timeout backstop elapses (hand off to readiness); otherwise Unhealthy. It makes no CMK /
    /// Key Vault call — CMK routability is handled by the readiness data-store check.
    /// </summary>
    public class StorageInitializedHealthCheck : IHealthCheck, INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly StorageInitializedHealthCheckConfiguration _configuration;
        private readonly DateTimeOffset _started = Clock.UtcNow;
        private volatile bool _storageReady;

        private const string SuccessfullyInitializedMessage = "Successfully initialized.";

        public StorageInitializedHealthCheck(IOptions<StorageInitializedHealthCheckConfiguration> configuration)
        {
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_storageReady)
            {
                return Task.FromResult(HealthCheckResult.Healthy(SuccessfullyInitializedMessage));
            }

            TimeSpan waited = Clock.UtcNow - _started;
            if (waited >= _configuration.StorageInitializationTimeout)
            {
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Startup timeout elapsed after {(int)waited.TotalSeconds}s; handing off to readiness."));
            }

            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                $"Storage is initializing. Waited: {(int)waited.TotalSeconds}s."));
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj --filter FullyQualifiedName~StorageInitializedHealthCheckTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Microsoft.Health.Fhir.Api/Features/Health/StorageInitializedHealthCheck.cs src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Features/Health/StorageInitializedHealthCheckTests.cs
git commit -m "refactor(health): make StorageInitializedHealthCheck a pure startup gate"
```

---

## Task 5: Bind config with `ValidateOnStart`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Web/Startup.cs:84-85`

- [ ] **Step 1: Replace the `Configure` call with a validated options binding**

Find (lines 84-85):

```csharp
            services.Configure<StorageInitializedHealthCheckConfiguration>(
                Configuration.GetSection(StorageInitializedHealthCheckConfiguration.SectionName));
```

Replace with:

```csharp
            services.AddOptions<StorageInitializedHealthCheckConfiguration>()
                .Bind(Configuration.GetSection(StorageInitializedHealthCheckConfiguration.SectionName))
                .Validate(
                    c => c.StorageInitializationTimeout >= TimeSpan.Zero,
                    $"{nameof(StorageInitializedHealthCheckConfiguration.StorageInitializationTimeout)} must be non-negative.")
                .ValidateOnStart();
```

- [ ] **Step 2: Verify `System` and options usings exist**

Run: `rg -n "using System;|using Microsoft.Extensions.DependencyInjection;" src/Microsoft.Health.Fhir.Shared.Web/Startup.cs`
Expected: both present. `AddOptions`/`Bind`/`Validate`/`ValidateOnStart` live in `Microsoft.Extensions.DependencyInjection` (already imported). If `using System;` is missing, add it.

- [ ] **Step 3: Build the Web project**

Run: `dotnet build src/Microsoft.Health.Fhir.Shared.Web/Microsoft.Health.Fhir.Shared.Web.csproj -clp:ErrorsOnly`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Microsoft.Health.Fhir.Shared.Web/Startup.cs
git commit -m "feat(health): validate storage-init config on start"
```

---

## Task 6: Thread-safe `ImproperBehaviorHealthCheck` (TDD)

**Files:**
- Create: `src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheckState.cs`
- Modify: `src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheck.cs`
- Test: `src/Microsoft.Health.Fhir.Api.UnitTests/Features/Health/ImproperBehaviorHealthCheckTests.cs`

- [ ] **Step 1: Add the failing concurrency test**

Append this test to the existing `ImproperBehaviorHealthCheckTests` class (after the existing `[Theory]` method, before the closing brace of the class). It asserts the `(isHealthy, message)` pair is always observed consistently — an unhealthy result must always carry the notification message.

```csharp
        [Fact]
        public async Task GivenConcurrentNotificationsAndProbes_WhenCheckingHealth_ThenStateIsConsistent()
        {
            using var cts = new System.Threading.CancellationTokenSource();

            Task reader = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    HealthCheckResult result = await _healthCheck.CheckHealthAsync(null, CancellationToken.None);
                    if (result.Status == HealthStatus.Unhealthy)
                    {
                        // An unhealthy result must always carry the appended message (no torn read).
                        Assert.Contains("boom", result.Description);
                    }
                }
            });

            await _healthCheck.Handle(new ImproperBehaviorNotification("boom"), CancellationToken.None);
            cts.Cancel();
            await reader;

            HealthCheckResult final = await _healthCheck.CheckHealthAsync(null, CancellationToken.None);
            Assert.Equal(HealthStatus.Unhealthy, final.Status);
            Assert.Contains("boom", final.Description);
        }
```

- [ ] **Step 2: Run the test to confirm it compiles and passes against current code (baseline)**

Run: `dotnet test src/Microsoft.Health.Fhir.Api.UnitTests/Microsoft.Health.Fhir.Api.UnitTests.csproj --filter FullyQualifiedName~ImproperBehaviorHealthCheckTests`
Expected: PASS (the race is timing-dependent and may pass even against the unsynced code). This test documents the invariant; the refactor below makes it hold deterministically. Proceed regardless of baseline result.

- [ ] **Step 3: Create the immutable state record**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Immutable snapshot of the improper-behavior health state so the health flag and its
    /// accompanying message are always published together atomically.
    /// </summary>
    internal sealed record ImproperBehaviorHealthCheckState(bool IsHealthy, string Message)
    {
        public static readonly ImproperBehaviorHealthCheckState Healthy = new(true, string.Empty);
    }
}
```

- [ ] **Step 4: Replace `ImproperBehaviorHealthCheck.cs` with a lock-guarded immutable swap**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Core.Features.Health;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    public class ImproperBehaviorHealthCheck : IHealthCheck, INotificationHandler<ImproperBehaviorNotification>
    {
        private readonly object _lock = new();
        private volatile ImproperBehaviorHealthCheckState _state = ImproperBehaviorHealthCheckState.Healthy;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ImproperBehaviorHealthCheckState state = _state;
            if (state.IsHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy());
            }

            return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, "Improper server behavior has been detected." + state.Message));
        }

        public Task Handle(ImproperBehaviorNotification notification, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _state = new ImproperBehaviorHealthCheckState(false, _state.Message + " " + notification.Message);
            }

            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: Run the behavior tests to verify they pass**

Run: `dotnet test src/Microsoft.Health.Fhir.Api.UnitTests/Microsoft.Health.Fhir.Api.UnitTests.csproj --filter FullyQualifiedName~ImproperBehaviorHealthCheckTests`
Expected: PASS (existing theory + new concurrency test).

- [ ] **Step 6: Commit**

```bash
git add src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheckState.cs src/Microsoft.Health.Fhir.Api/Features/Health/ImproperBehaviorHealthCheck.cs src/Microsoft.Health.Fhir.Api.UnitTests/Features/Health/ImproperBehaviorHealthCheckTests.cs
git commit -m "fix(health): make ImproperBehaviorHealthCheck state publication atomic"
```

---

## Task 7: Apply probe tags at registration

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs:196` and `:206`
- Modify: `src/Microsoft.Health.Fhir.CosmosDb/Registration/FhirServerBuilderCosmosDbRegistrationExtensions.cs:316-317`

- [ ] **Step 1: Tag the Behavior and Storage checks in `FhirModule.cs`**

Confirm the file has `using Microsoft.Health.Fhir.Api.Features.Health;` (the checks live there). If missing, add it with the other usings.

Find (line 196):

```csharp
            services.AddHealthChecks().AddCheck<ImproperBehaviorHealthCheck>(name: "BehaviorHealthCheck");
```

Replace with:

```csharp
            services.AddHealthChecks().AddCheck<ImproperBehaviorHealthCheck>(
                name: "BehaviorHealthCheck",
                tags: new[] { HealthCheckTags.ProbeReadiness });
```

Find (line 206):

```csharp
            services.AddHealthChecks().AddCheck<StorageInitializedHealthCheck>(name: "StorageInitializedHealthCheck");
```

Replace with:

```csharp
            services.AddHealthChecks().AddCheck<StorageInitializedHealthCheck>(
                name: "StorageInitializedHealthCheck",
                tags: new[] { HealthCheckTags.ProbeStartup });
```

- [ ] **Step 2: Tag the Cosmos data-store check**

In `FhirServerBuilderCosmosDbRegistrationExtensions.cs`, confirm `using Microsoft.Health.Fhir.Api.Features.Health;` is present (add if missing). Find (lines 316-317):

```csharp
            fhirServerBuilder.Services.AddHealthChecks()
                .AddCheck<CosmosDbHealthCheck>(name: "DataStoreHealthCheck");
```

Replace with:

```csharp
            fhirServerBuilder.Services.AddHealthChecks()
                .AddCheck<CosmosDbHealthCheck>(
                    name: "DataStoreHealthCheck",
                    tags: new[] { HealthCheckTags.ProbeReadiness });
```

- [ ] **Step 3: Build the touched projects**

Run: `dotnet build src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.csproj -clp:ErrorsOnly` and `dotnet build src/Microsoft.Health.Fhir.CosmosDb/Microsoft.Health.Fhir.CosmosDb.csproj -clp:ErrorsOnly`
Expected: Build succeeded for both.

- [ ] **Step 4: Commit**

```bash
git add src/Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs src/Microsoft.Health.Fhir.CosmosDb/Registration/FhirServerBuilderCosmosDbRegistrationExtensions.cs
git commit -m "feat(health): tag health checks for startup/readiness probes"
```

---

## Task 8: Map four endpoints + shared response writer + startup assertion

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Api/Registration/FhirServerApplicationBuilderExtensions.cs`

- [ ] **Step 1: Replace the `UseFhirServer` body and add helpers**

Replace the whole `UseFhirServer` method (lines 34-89) plus add the two private helpers below. This: (a) validates registrations at startup, (b) maps the four routes each with a tag predicate and explicit status map, (c) extracts the JSON writer.

Replace the method (from `public static IApplicationBuilder UseFhirServer(` through its closing `return app; }`) with:

```csharp
        public static IApplicationBuilder UseFhirServer(
            this IApplicationBuilder app,
            Func<IApplicationBuilder, IApplicationBuilder> useDevelopmentIdentityProvider = null,
            Func<IApplicationBuilder, IApplicationBuilder> useHttpLoggingMiddleware = null,
            Func<HealthCheckRegistration, bool> healthCheckOptionsPredicate = null,
            Func<IEndpointRouteBuilder, IEndpointRouteBuilder> mapAdditionalEndpoints = null)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            var config = app.ApplicationServices.GetRequiredService<IOptions<FhirServerConfiguration>>();
            var pathBase = config.Value.PathBase?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(pathBase))
            {
                var pathString = new PathString(pathBase);
                app.UseMiddleware<PathBaseMiddleware>(pathString);
            }

            ValidateHealthCheckRegistrations(app.ApplicationServices);

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            useDevelopmentIdentityProvider?.Invoke(app);
            useHttpLoggingMiddleware?.Invoke(app);

            app.UseEndpoints(
                endpoints =>
                {
                    endpoints.MapControllers();

                    // Diagnostic endpoint: everything except the startup gate. Degraded => 200.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheck),
                        new HealthCheckOptions
                        {
                            Predicate = reg => (healthCheckOptionsPredicate?.Invoke(reg) ?? true)
                                && !reg.Tags.Contains(HealthCheckTags.ProbeStartup),
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Startup gate: only the storage-init check. Unhealthy => 503 while initializing.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckStartup),
                        new HealthCheckOptions
                        {
                            Predicate = reg => reg.Tags.Contains(HealthCheckTags.ProbeStartup),
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Readiness/routing: data-store + behavior. Degraded (e.g. CMK) => 200 stays routable.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckReady),
                        new HealthCheckOptions
                        {
                            Predicate = reg => reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer)
                                || reg.Tags.Contains(HealthCheckTags.ProbeReadiness),
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Dependency-free HTTP liveness: run no checks => empty report => Healthy => 200.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckLive),
                        new HealthCheckOptions
                        {
                            Predicate = _ => false,
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    mapAdditionalEndpoints?.Invoke(endpoints);
                });

            return app;
        }

        private static async Task WriteHealthReportAsync(HttpContext httpContext, HealthReport healthReport)
        {
            var response = JsonConvert.SerializeObject(
                new
                {
                    overallStatus = healthReport.Status.ToString(),
                    details = healthReport.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = Enum.GetName<HealthStatus>(entry.Value.Status),
                        description = entry.Value.Description,
                        data = entry.Value.Data,
                    }),
                });
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;
            await httpContext.Response.WriteAsync(response).ConfigureAwait(false);
        }

        private static void ValidateHealthCheckRegistrations(IServiceProvider services)
        {
            var options = services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

            bool ReadinessPredicate(HealthCheckRegistration reg) =>
                reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer) || reg.Tags.Contains(HealthCheckTags.ProbeReadiness);

            int dataStoreCount = options.Registrations.Count(
                reg => ReadinessPredicate(reg) && string.Equals(reg.Name, "DataStoreHealthCheck", StringComparison.Ordinal));
            if (dataStoreCount != 1)
            {
                throw new InvalidOperationException(
                    $"Readiness probe must resolve exactly one 'DataStoreHealthCheck' registration but resolved {dataStoreCount}. " +
                    "This usually indicates a health-check tag typo or a healthcare-shared-components tag rename/package skew.");
            }

            int startupCount = options.Registrations.Count(reg => reg.Tags.Contains(HealthCheckTags.ProbeStartup));
            if (startupCount != 1)
            {
                throw new InvalidOperationException(
                    $"Startup probe must resolve exactly one registration but resolved {startupCount}.");
            }
        }
```

- [ ] **Step 2: Add required usings and the `HealthCheckTags` import**

At the top of the file confirm/add:
- `using System.Collections.Generic;` (may already be transitively fine; add if the build complains)
- `using Microsoft.Extensions.Diagnostics.HealthChecks;` (already present)
- `using Microsoft.Health.Fhir.Api.Features.Health;` (NEW — for `HealthCheckTags`)

`HealthCheckServiceOptions` lives in `Microsoft.Extensions.Diagnostics.HealthChecks` (already imported). `HealthReport`/`HealthStatus` are in the same namespace.

- [ ] **Step 3: Build the Api project**

Run: `dotnet build src/Microsoft.Health.Fhir.Api/Microsoft.Health.Fhir.Api.csproj -clp:ErrorsOnly`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Microsoft.Health.Fhir.Api/Registration/FhirServerApplicationBuilderExtensions.cs
git commit -m "feat(health): map startup/ready/live endpoints with tag predicates and startup assertion"
```

---

## Task 9: Registration & assertion tests

**Files:**
- Create: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckRegistrationTests.cs`

These tests exercise the exact readiness/startup predicates against `HealthCheckServiceOptions.Registrations` (the same source of truth `ValidateHealthCheckRegistrations` reads) to catch tag drift.

- [ ] **Step 1: Write the tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class HealthCheckRegistrationTests
{
    private sealed class StubCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }

    private static bool Readiness(HealthCheckRegistration reg) =>
        reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer) || reg.Tags.Contains(HealthCheckTags.ProbeReadiness);

    [Fact]
    public void GivenSqlDataStoreTag_WhenResolvingReadiness_ThenExactlyOneDataStoreCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>("DataStoreHealthCheck", tags: new[] { HealthCheckTags.DataStoreSqlServer })
            .AddCheck<StubCheck>("BehaviorHealthCheck", tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        int dataStoreCount = options.Registrations.Count(r => Readiness(r) && r.Name == "DataStoreHealthCheck");
        Assert.Equal(1, dataStoreCount);
        Assert.Equal(1, options.Registrations.Count(r => r.Tags.Contains(HealthCheckTags.ProbeStartup)));
    }

    [Fact]
    public void GivenCosmosReadinessTag_WhenResolvingReadiness_ThenExactlyOneDataStoreCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>("DataStoreHealthCheck", tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("BehaviorHealthCheck", tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Equal(1, options.Registrations.Count(r => Readiness(r) && r.Name == "DataStoreHealthCheck"));
    }

    [Fact]
    public void GivenMissingDataStoreTag_WhenResolvingReadiness_ThenZeroDataStoreChecks()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>("DataStoreHealthCheck") // no tag => package-skew simulation
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Equal(0, options.Registrations.Count(r => Readiness(r) && r.Name == "DataStoreHealthCheck"));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj --filter FullyQualifiedName~HealthCheckRegistrationTests`
Expected: PASS (3 tests).

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckRegistrationTests.cs
git commit -m "test(health): registration tag-resolution tests for readiness/startup"
```

---

## Task 10: Endpoint routing/status tests (TestServer)

**Files:**
- Create: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckEndpointTests.cs`

These build a minimal ASP.NET Core pipeline (`WebHostBuilder` + `TestServer`) that maps the four routes using the same predicates as `UseFhirServer`, registers stub checks, and asserts HTTP codes. This validates the routing/predicate/default-status-map behavior end-to-end without booting the full FHIR server.

- [ ] **Step 1: Write the endpoint tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class HealthCheckEndpointTests
{
    private sealed class FixedCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public FixedCheck(HealthStatus status) => _status = status;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(_status));
    }

    private static TestServer BuildServer(HealthStatus startupStatus, HealthStatus dataStoreStatus)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHealthChecks()
                    .AddCheck("StorageInitializedHealthCheck", new FixedCheck(startupStatus), tags: new[] { HealthCheckTags.ProbeStartup })
                    .AddCheck("DataStoreHealthCheck", new FixedCheck(dataStoreStatus), tags: new[] { HealthCheckTags.ProbeReadiness })
                    .AddCheck("BehaviorHealthCheck", new FixedCheck(HealthStatus.Healthy), tags: new[] { HealthCheckTags.ProbeReadiness });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHealthChecks("/health/check", new HealthCheckOptions
                    {
                        Predicate = reg => !reg.Tags.Contains(HealthCheckTags.ProbeStartup),
                    });
                    endpoints.MapHealthChecks("/health/startup", new HealthCheckOptions
                    {
                        Predicate = reg => reg.Tags.Contains(HealthCheckTags.ProbeStartup),
                    });
                    endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                    {
                        Predicate = reg => reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer)
                            || reg.Tags.Contains(HealthCheckTags.ProbeReadiness),
                    });
                    endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                    {
                        Predicate = _ => false,
                    });
                });
            });

        return new TestServer(builder);
    }

    [Fact]
    public async Task GivenInitializingStartup_WhenGettingStartup_Then503()
    {
        using TestServer server = BuildServer(HealthStatus.Unhealthy, HealthStatus.Healthy);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/startup");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GivenInitializedStartup_WhenGettingStartup_Then200()
    {
        using TestServer server = BuildServer(HealthStatus.Healthy, HealthStatus.Healthy);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/startup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenDegradedDataStore_WhenGettingReady_Then200()
    {
        using TestServer server = BuildServer(HealthStatus.Healthy, HealthStatus.Degraded);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenUnhealthyDataStore_WhenGettingReady_Then503()
    {
        using TestServer server = BuildServer(HealthStatus.Healthy, HealthStatus.Unhealthy);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GivenAnyState_WhenGettingLive_Then200()
    {
        using TestServer server = BuildServer(HealthStatus.Unhealthy, HealthStatus.Unhealthy);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenInitializingStartup_WhenGettingCheck_ThenStartupGateExcluded_And200()
    {
        // /health/check excludes probe:startup, so an initializing pod with a reachable DB is 200.
        using TestServer server = BuildServer(HealthStatus.Unhealthy, HealthStatus.Healthy);
        HttpResponseMessage response = await server.CreateClient().GetAsync("/health/check");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Verify the test project can host a TestServer**

Run: `rg -n "TestHost|Mvc.Testing|Microsoft.AspNetCore.App" src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj`
Expected: a `FrameworkReference Include="Microsoft.AspNetCore.App"` and a `Microsoft.AspNetCore.TestHost` (or `Mvc.Testing`) reference. If missing: add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` and `<PackageReference Include="Microsoft.AspNetCore.TestHost" />` (version from `Directory.Packages.props`; check with `rg -n "AspNetCore.TestHost" Directory.Packages.props`). If `TestHost` is not pinned anywhere in the repo, DELETE this endpoint-test file and rely on Task 9's predicate tests — note the endpoint-level assertions are then covered by the fhir-paas E2E suite. Do not add a brand-new external dependency without confirming it is already used in the repo.

- [ ] **Step 3: Run the endpoint tests**

Run: `dotnet test src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj --filter FullyQualifiedName~HealthCheckEndpointTests`
Expected: PASS (6 tests).

- [ ] **Step 4: Commit**

```bash
git add src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/HealthCheckEndpointTests.cs
git commit -m "test(health): endpoint routing and status-map tests for four probes"
```

---

## Task 11: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole affected solution graph**

Run: `dotnet build src/Microsoft.Health.Fhir.Shared.Web/Microsoft.Health.Fhir.Shared.Web.csproj -clp:ErrorsOnly`
Expected: Build succeeded (transitively builds Api, Core, Shared.Api, CosmosDb, SqlServer).

- [ ] **Step 2: Run the two health-check test projects in full**

Run: `dotnet test src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.csproj --filter "FullyQualifiedName~Health"`
Then: `dotnet test src/Microsoft.Health.Fhir.Api.UnitTests/Microsoft.Health.Fhir.Api.UnitTests.csproj --filter "FullyQualifiedName~Health"`
Expected: PASS across `StorageInitializedHealthCheckTests`, `ImproperBehaviorHealthCheckTests`, `HealthCheckRegistrationTests`, `HealthCheckEndpointTests`.

- [ ] **Step 3: Grep for stale references to the removed knob / dropped dependency**

Run: `rg -n "StartupDegradedDelay" src`
Expected: zero hits (the config knob is fully removed).
Run: `rg -n "IDatabaseStatusReporter" src/Microsoft.Health.Fhir.Api/Features/Health`
Expected: zero hits (the CMK dependency is removed from the startup gate; it may still exist elsewhere).

- [ ] **Step 4: Confirm all four routes are wired**

Run: `rg -n "HealthCheck(Startup|Ready|Live)?\b" src/Microsoft.Health.Fhir.Core/Features/Routing/KnownRoutes.cs src/Microsoft.Health.Fhir.Api/Registration/FhirServerApplicationBuilderExtensions.cs`
Expected: four constants defined; four `MapHealthChecks` calls.

- [ ] **Step 5: Final commit if any residual fixes were needed**

```bash
git add -A
git commit -m "chore(health): finalize separate-probe verification" || echo "nothing to commit"
```

---

## Post-Plan Notes (out of scope for this plan)

- **fhir-paas companion (REQUIRED, separate subsession):** three distinct `ExecAction`s pointing at `/health/startup`, `/health/ready`, `/health/live`; suspended-account + private-link allowlists widened to all four health paths; ingress suspension rule widened; startup budget raised above the 5-minute app timeout (`failureThreshold × periodSeconds` > `StorageInitializationTimeout` + margin, e.g. `6×7 × 10s ≈ 7:00`). Suspended pods crash-loop until the middleware allowlists accept the new probes, so this must ship atomically. See spec "Deferred" section.
- **K8s budget invariant:** `legit-init-p99 (≈5m) < StorageInitializationTimeout (5m) < k8s-startup-budget`. fhir-server keeps the app timeout at 5 minutes; the K8s budget bump lives in fhir-paas and cannot be enforced from this repo.
