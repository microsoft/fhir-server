# .NET 10 Upgrade & MediatR → Medino Migration — Design

- **Date:** 2026-06-15
- **Repo:** microsoft/fhir-server
- **Status:** Approved design (ready for implementation planning)
- **Reference precedent:** dicom-server commit `ad152fdba8378908614801c564873c0b9ce0c2e4` ("Upgrade dicom/app and dicom/provision to .NET 10 + MISE 1.42.0")
- **Tracking:** ADO Story #195645 (tasks #195646 Planning & communication, #195647 PR 1 — Medino migration)

---

## 1. Goal

Upgrade the FHIR Server repository to **.NET 10** and replace **MediatR** (commercial from 12.x onward) with **Medino** — the MIT-licensed, drop-in successor to MediatR 12.5 maintained by Brendan Kowitz. The work is split into two sequential pull requests to keep each diff small and reviewable.

## 2. Key decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Target frameworks | **`net10.0;net8.0`** (drop `net9.0`) | net8.0 stays as the downlevel floor. The fhir-paas RP consumes `Microsoft.Health.Fhir.*` NuGet packages from its `LatestVersion` (net9.0 today) projects and resolves the net8.0 asset via downlevel. A net10-only build would break those consumers. |
| Sequencing | **PR 1 = Medino + shared 11.x, PR 2 = .NET 10** | The mediator and shared-package compatibility work is de-risked on the current toolchain before the framework bump. PR 2 can then focus on TFMs, SDK, pipelines, Docker, and dev container changes. |
| Medino execution | **Compiler-driven big-bang** in PR 1 | The package swap makes the compiler flag every call/handler site needing a rename, turning the migration into a deterministic checklist. |
| Shared packages | **Bump `HealthcareSharedPackageVersion` 10.0.68 → 11.0.111** in PR 1 | Shared 11.x removes the `Microsoft.Health.SqlServer` MediatR schema-upgrade boundary, allowing PR 1 to eliminate active MediatR references while still building the current `net9.0;net8.0` TFMs. |
| Scope | **Tight** | No `.sln → .slnx` migration and no StyleCop removal, even though the dicom precedent included both. |

## 3. Why Medino is self-contained

- fhir-server uses **no streaming** (`IStreamRequestHandler`) and **no MediatR `Unit`** type — the two notable Medino feature gaps do not apply.
- Shared `Microsoft.Health.SqlServer` 10.x exposed a MediatR schema-upgrade boundary; moving to shared 11.x in PR 1 removes that bridge and keeps active mediation on Medino.
- Medino has **no `IRequestPreProcessor`/`IRequestPostProcessor`** — the only non-mechanical rework (see PR 1, §4.3).

---

## 4. PR 1 — MediatR → Medino migration + shared 11.x

Performed on the **current `net9.0;net8.0`** toolchain. No framework change.

### 4.1 Package swap (`Directory.Packages.props`)
- Remove `MediatR` `12.5.0`.
- Add `Medino` `3.0.3` and `Medino.Extensions.DependencyInjection` `3.0.3` (latest stable on nuget.org, verified).
- Bump `HealthcareSharedPackageVersion` to `11.0.111` and align required transitive pins (`DotNetSdkPackageVersion` `10.0.9`, `Azure.Identity` `1.21.0`, `Microsoft.Data.SqlClient` `7.0.1`) so restore has no package downgrades.

### 4.2 Mechanical renames (~210 files, compiler-verified)
- `using MediatR;` / `using MediatR.Pipeline;` → `using Medino;`.
- Mediator calls (scoped to `_mediator.` / `mediator.` / `Mediator.` to avoid false positives):
  - `.Send(` → `.SendAsync(` (~126 sites).
  - `.Publish(` → `.PublishAsync(` (~53 sites).
- Handler / notification / pipeline-behavior methods: `Handle(...)` → `HandleAsync(...)` (~63 handlers + 21 behaviors). Interface names (`IRequestHandler<,>`, `INotificationHandler<>`, `IPipelineBehavior<,>`, `IRequest<T>`) are unchanged.
- `SqlExceptionActionProcessor` (`IRequestExceptionAction<TRequest,TException>`): `Execute(...)` → `ExecuteAsync(...)`.

### 4.3 Hand-written rework (non-mechanical)
Medino has no `IRequestPreProcessor`. Convert the three validators into Medino `BeforePipelineBehavior<TRequest, TResponse>` (open generic), moving the `Process(...)` body into `BeforeAsync(...)` and preserving the throw-to-short-circuit semantics:
- `ValidateRequestPreProcessor<TRequest>` (FluentValidation; throws `ResourceNotValidException`).
- `ValidateBundlePreProcessor` (`IRequestPreProcessor<BundleRequest>`; throws `RequestNotValidException`).
- `ValidateCapabilityPreProcessor<TRequest>`.

### 4.4 `MediationModule.cs`
- Replace `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(KnownAssemblies.All))` with `services.AddMedino(KnownAssemblies.All)`.
- **Delete** the two explicit exception-processor registrations (`RequestExceptionActionProcessorBehavior<,>`, `RequestExceptionProcessorBehavior<,>`) — Medino auto-wires registered `IRequestExceptionAction`/`IRequestExceptionHandler` implementations.
- Register the three new validation `BeforePipelineBehavior`s.
- The `IProvideCapability` scan block (over `IRequestHandler<,>`/`INotificationHandler<>` via `TypesInSameAssembly(KnownAssemblies.All)`) is framework-agnostic and stays unchanged.

### 4.5 Verification
- Confirm the ~19 other `IPipelineBehavior<,>` registrations (declared in fhir's own DI modules) resolve from the DI container under Medino — they need only the `Handle → HandleAsync` rename.
- Full unit + integration suite green on `net9.0;net8.0`.

### 4.6 Acceptance criteria
- Builds clean on `net9.0;net8.0` with **no remaining `MediatR` references**.
- All tests green.
- Validation pre-processor behavior preserved (verified by existing validation tests).

---

## 5. PR 2 — .NET 10 upgrade

Lands **after** PR 1 is merged, so the diff is purely framework/tooling with no business-logic churn.

### 5.1 SDK & target frameworks
- `global.json`: SDK `9.0.314` → **`10.0.300`** (or latest stable 10.0.x at implementation time).
- `build/dotnet8-compat/global.json`: **stays `8.0.421`** (drives the net8.0 downlevel build).
- `Directory.Build.props`: `<TargetFrameworks>net9.0;net8.0</TargetFrameworks>` → **`net10.0;net8.0`**.

### 5.2 Packages (`Directory.Packages.props`)
- ASP.NET `<Choose>` block: replace the `net9.0 → 9.0.15` arm with **`net10.0 → 10.0.x`**; keep the `net8.0 → 8.0.26` arm.
- Shared `Microsoft.Health.*` and required transitive `Microsoft.Extensions.*` / `System.*` pins are already on the 11.x / 10.0.x line from PR 1; only adjust patch versions if the .NET 10 implementation requires newer servicing builds.
- **No MISE bump** — `Microsoft.Identity.ServiceEssentials` is not referenced by fhir-server (the dicom cert-fix step does not apply).
- `Microsoft.Identity.Web` (`2.13.3`) left as-is unless the build requires otherwise.

### 5.3 Pipelines & Docker
- `build/build-variables.yml`: `defaultBuildFramework 'net9.0' → 'net10.0'` — cascades to the ~7 jobs already using `$(defaultBuildFramework)` (export, package-web, package-integration-tests, cosmos/sql test jobs).
- `build/ci-pipeline.yml` + `build/pr-pipeline.yml`: rename job `Windows_dotnet9 → Windows_dotnet10` (cosmetic); the `Linux_dotnet8` job stays for downlevel coverage.
- `build/jobs/analyze.yml` (~L113): `-f net9.0` → `-f net10.0`.
- `build/.vsts-PRInternalChecks-azureBuild-pipeline.yml` (~L35): `-f net8.0` **stays**.
- `build/docker/Dockerfile`: `sdk:9.0.314-azurelinux3.0` → `sdk:10.0.x-azurelinux3.0`; `aspnet:9.0.16-azurelinux3.0` → `aspnet:10.0.x-azurelinux3.0`; `-f net9.0` → `-f net10.0`.
- `UseDotNet@2` tasks use `useGlobalJson: true`, so they auto-install `10.0.300` from `global.json` — no per-task version edits.

### 5.4 Dev container
- `.devcontainer/Dockerfile` installs the SDK via `dotnet-install.sh --jsonfile /setup/global.json`, so the SDK **auto-follows** the `global.json` bump to `10.0.300` — no version edit there.
- **Bump the base image** `mcr.microsoft.com/vscode/devcontainers/base:0-buster` (Debian 10, EOL — below .NET 10's OS minimum) to a supported distro (e.g. `mcr.microsoft.com/devcontainers/base:bookworm`), verifying the `library-scripts/*.sh` (common/docker/zsh) still apply on the newer base.
- Verify: the dev container rebuilds and `dotnet build` succeeds inside it on net10.0.

### 5.5 Testing / verification
- Local: `dotnet build` + full unit/integration suite on **both** `net10.0` and `net8.0`.
- CI proves both rails (`Windows_dotnet10` + `Linux_dotnet8`).
- Smoke-test the Docker image build (azurelinux3.0 sdk + aspnet base tags for 10.0.x must be published).
- Smoke-test the dev container build.

### 5.6 Acceptance criteria
- Solution builds clean on `net10.0;net8.0` with `TreatWarningsAsErrors=true`.
- All unit/integration tests green on both frameworks in CI.
- Docker image and dev container build successfully.

---

## 6. Risks & open items

| Risk / item | Mitigation |
|-------------|-----------|
| **Shared 10 → 11 major bump** may carry breaking API changes across the `Microsoft.Health.*` packages | Primary risk of PR 2; address breaks during implementation; lean on the compiler + tests. |
| **azurelinux3.0 base-image tags** for 10.0.x sdk/aspnet must be published | Verify tag availability before finalizing the Dockerfile. |
| **Dev container base-image bump** may need follow-up tweaks to `library-scripts` | Rebuild and validate the container as part of PR 2. |
| **`TreatWarningsAsErrors=true`** + net10 analyzers may surface new warnings that fail the build | Budget time to clear new analyzer warnings. |
| **Exact 10.0.x patch** for SDK/ASP.NET | Pin to the latest stable at implementation time (10.0.300 as the reference baseline). |
| **RP/fhir-paas coordination** — RP consumes fhir-server packages | Covered by the Planning & communication task (#195646): align with RP owners before/around the package version bump. |

## 7. Out of scope

- `.sln → .slnx` migration.
- StyleCop analyzer removal/replacement.
- Any functional/behavioral FHIR changes.
- RP/fhir-paas repository changes (coordination only).
