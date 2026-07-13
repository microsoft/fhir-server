# Separate Kubernetes Health Probes for the FHIR Server

- **Date:** 2026-07-13
- **Status:** Approved design (pre-implementation)
- **Repos in scope:** `microsoft/fhir-server`, `microsoft/healthcare-shared-components`
- **Deferred (separate subsession):** `fhir-paas` probe configuration
- **Supersedes:** the two-tier `StorageInitializedHealthCheck` logic committed in PR #5669

## Problem

The FHIR server exposes a single health endpoint, `/health/check`, that every
Kubernetes probe (startup, readiness, liveness) targets. ASP.NET Core maps a
`Degraded` health result to HTTP 200 by default, so during startup the pod is
marked ready and receives client traffic before storage initialization
completes. Work item AB#197936 captured the resulting window of HTTP 503
responses to real requests.

PR #5669 mitigated this by turning `/health/check` into a two-tier state machine
(Unhealthy for the first minute, Degraded until a five-minute timeout, then a
CMK-aware result). That change overloads a single diagnostic endpoint with probe
semantics, and it cannot satisfy two conflicting requirements at once:

1. The pod must **not** accept traffic until storage is initialized.
2. The pod must **not** crash-loop when a failure is persistent — most
   importantly a customer-managed key (CMK) problem, where the pod must stay
   routable so clients receive their HTTP 403 `OperationOutcome` rather than
   being pulled from rotation.

A single endpoint cannot express "don't route yet" and "stay alive / stay
routable" independently. The fix is to split the probes.

## Goals

- Separate startup, readiness, and liveness concerns into distinct endpoints,
  each with its own set of checks and HTTP status mapping.
- Prevent traffic before initialization completes (startup gate).
- Never crash-loop the pod on a persistent failure (CMK or DB inaccessible):
  startup completes and hands off to readiness instead of failing forever.
- Keep CMK-failed pods routable so customers receive their 403 `OperationOutcome`.
- Keep `/health/check` unchanged as the existing diagnostic endpoint.
- Revert PR #5669's Degraded-tier logic; move steady-state CMK/Degraded routing
  to the data-store check on the readiness route.

## Non-Goals

- Changing `fhir-paas` probe configuration. That is a documented companion
  change handled in a later subsession once this work merges upstream.
- Introducing health-check response caching for the new endpoints.
- Altering CMK detection semantics in the data-store checks themselves.

## Design Overview

Four routes are mapped in
`FhirServerApplicationBuilderExtensions.UseFhirServer`, each filtered by a
health-check **tag** and given an explicit `ResultStatusCodes` map. Check-to-probe
membership is declared at registration time via tags, following the existing
`datastore:sqlServer` tag convention already present in
healthcare-shared-components.

### Endpoints

| Route | Checks (tag predicate) | HTTP mapping | Purpose |
|---|---|---|---|
| `/health/check` | all checks (existing caller predicate) | Healthy/Degraded → 200, Unhealthy → 503 | Existing diagnostic endpoint — unchanged |
| `/health/startup` | `probe:startup` → `StorageInitializedHealthCheck` only | Healthy → 200, Degraded → 503, Unhealthy → 503 | Gate: 503 while initializing; 200 once init done or terminal failure/backstop |
| `/health/ready` | `probe:readiness` → `DataStoreHealthCheck` + `BehaviorHealthCheck` | Healthy/Degraded → 200, Unhealthy → 503 | Routing decision; CMK `Degraded` stays routable |
| `/health/live` | none (predicate → `false`) | always 200 (no checks → Healthy) | Process-alive |

New route constants in `KnownRoutes.cs`: `HealthCheckStartup` (`/health/startup`),
`HealthCheckReady` (`/health/ready`), `HealthCheckLive` (`/health/live`).

The `UseFhirServer` signature is unchanged: the startup/readiness/liveness
predicates are internal constants; only `/health/check` keeps the caller-supplied
`healthCheckOptionsPredicate`.

### Startup gate: `StorageInitializedHealthCheck`

The check becomes a pure startup gate. It returns only **Healthy** (startup done
or handoff) or **Unhealthy** (still initializing) — never Degraded. All
Degraded/CMK routing behavior moves to `DataStoreHealthCheck` on the readiness
route.

Configuration (`StorageInitializedHealthCheckConfiguration`, bound from
`HealthChecks:StorageInitialization`, repurposed from PR #5669):

- `TerminalFailureGracePeriod` — default **1 minute**. Minimum time to wait
  before a detected terminal failure is honored, allowing the CMK health cache to
  populate and transient boot states to clear (prevents a false early handoff).
- `StartupTimeout` — default **5 minutes**. Absolute backstop; after this the
  gate hands off regardless of state.
- Validation: both non-negative; `TerminalFailureGracePeriod` ≤ `StartupTimeout`.

State machine in `CheckHealthAsync` (uses the existing `Clock` abstraction and the
`SearchParametersInitializedNotification` handler that sets `_storageReady`):

1. `_storageReady` → **Healthy** ("Successfully initialized.").
2. `waited < TerminalFailureGracePeriod` → **Unhealthy** ("initializing").
3. `waited ≥ TerminalFailureGracePeriod` **and** terminal failure detected
   (`IDatabaseStatusReporter.IsCustomerManagerKeyProperlySetAsync` returns false)
   → **Healthy** ("startup complete: storage inaccessible, handing off to
   readiness").
4. `waited < StartupTimeout` → **Unhealthy** ("initializing").
5. `waited ≥ StartupTimeout` → **Healthy** ("startup timeout elapsed, handing
   off to readiness").

Net behavior: `/health/startup` returns 503 only while genuinely initializing,
and flips to 200 on init success, terminal CMK failure (after the grace period),
or the timeout backstop. The pod is never crash-looped by the startup probe;
once startup succeeds, Kubernetes stops calling it and readiness governs routing.

### Readiness

`/health/ready` mirrors the current `/health/check` mapping: `Healthy`/`Degraded`
→ 200, `Unhealthy` → 503. A CMK problem surfaces as `Degraded` from
`DataStoreHealthCheck`, so the pod stays routable (200) and clients receive their
403 `OperationOutcome`. Only a genuine `Unhealthy` result (for example a SQL
transport error that clears the connection pools) pulls the pod from rotation.

### Liveness

`/health/live` runs zero checks (predicate returns `false`), so the report is
empty and resolves to `Healthy` → HTTP 200 whenever the process is up. This gives
`fhir-paas` the choice of an HTTP liveness probe or a `tcpSocket` probe later.

## Tagging and Registration Changes

Tag convention (plain string literals, matching existing `datastore:sqlServer`):
`probe:startup`, `probe:readiness`. Liveness uses no tag.

| Check | File | Tag added |
|---|---|---|
| `StorageInitializedHealthCheck` | `Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` (~206) | `probe:startup` |
| `ImproperBehaviorHealthCheck` | `Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` (~196) | `probe:readiness` |
| `CosmosDbHealthCheck` (`DataStoreHealthCheck`) | `Microsoft.Health.Fhir.CosmosDb/Registration/FhirServerBuilderCosmosDbRegistrationExtensions.cs` (~317) | `probe:readiness` |
| SQL `DataStoreHealthCheck` | **shared** `Microsoft.Health.SqlServer.Api/Registration/SqlServerApiRegistrationExtensions.cs` (~19) | add `probe:readiness` to `SqlServerHealthCheckTags` |

The shared-repo change is a single-line tag addition:
`SqlServerHealthCheckTags = { "datastore:sqlServer", "probe:readiness" }`.

The parallel `.AsService<IHealthCheck>()` DI registrations are untouched;
ASP.NET's `HealthCheckService` executes only the `AddCheck` registrations, so no
check runs twice.

## Response Writer

The JSON response writer is currently inlined once in the single `MapHealthChecks`
call. Extract it to a private `WriteHealthReportAsync(HttpContext, HealthReport)`
helper in `FhirServerApplicationBuilderExtensions` and reuse it across all four
routes. The body shape for `/health/check` is unchanged (`overallStatus` +
per-entry `name`/`status`/`description`/`data`).

## Error Handling and Edge Cases

- **Terminal CMK then recovery before init:** once the gate hands off (Healthy),
  Kubernetes stops calling the startup probe. Readiness continues to reflect real
  state and routes or pulls the pod accordingly.
- **Init completes after handoff:** `_storageReady` becomes true; all checks
  report Healthy. No special handling needed.
- **Concurrent probe execution:** each probe runs only its tag-matched checks
  (startup: one cheap check; readiness: a SQL/Cosmos query plus behavior;
  liveness: none). Checks are re-entrant; periodic probe cadence makes this
  inexpensive.
- **Transient CMK-uninitialized read at boot:** guarded by
  `TerminalFailureGracePeriod` so an unpopulated CMK cache cannot trigger a false
  early handoff.

## Testing

Unit tests — `StorageInitializedHealthCheck` (existing test file, fake `Clock`
with millisecond-scale configuration):

- `_storageReady` → Healthy immediately, even before the grace period.
- Not ready, `waited < grace`, CMK bad → Unhealthy (grace protects transient state).
- Not ready, `waited ≥ grace`, CMK not properly set → Healthy (terminal handoff).
- Not ready, `grace ≤ waited < timeout`, CMK fine → Unhealthy (still initializing).
- Not ready, `waited ≥ timeout`, CMK fine → Healthy (backstop handoff).
- Config validation: negative grace or timeout throws; grace > timeout throws.

Endpoint/routing tests (fhir-server, `TestServer` / `WebApplicationFactory` where
feasible): request `/health/startup`, `/health/ready`, `/health/live` and assert
the status-code mapping and that only tag-matched checks run — for example
startup 503 while initializing then 200 after handoff, readiness 200 on a
CMK-`Degraded` data store, and liveness 200 with zero checks.

Shared repo: the tag addition needs no new test; existing SQL health-check tests
must still pass. Optionally assert the registration carries `probe:readiness`.

## Configuration Migration

Replace PR #5669's field names `StartupDegradedDelay` and
`StorageInitializationTimeout` with `TerminalFailureGracePeriod` and
`StartupTimeout`. Update any `appsettings.json` entries under
`HealthChecks:StorageInitialization`. Risk is low because PR #5669 is not merged.

## Deferred: fhir-paas Probe Configuration

Handled in a later subsession after this work merges upstream. In
`fhiroperator/controllers/fhir_controller.go`, repoint the probes:

- Startup probe → `/health/startup`, `failureThreshold` sized to the ~5-minute
  `StartupTimeout` backstop.
- Readiness probe → `/health/ready`.
- Liveness probe → `/health/live` (or a `tcpSocket` probe).

## Rollout Considerations

While the new endpoints exist but `fhir-paas` still points every probe at
`/health/check`, behavior is unchanged from the (reverted-to-simple) baseline —
the new routes are additive and unused until `fhir-paas` is updated. This lets
the fhir-server and healthcare-shared-components changes merge and release
independently of the probe-configuration change.
