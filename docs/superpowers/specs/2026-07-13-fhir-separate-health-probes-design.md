# Separate Kubernetes Health Probes for the FHIR Server

- **Date:** 2026-07-13
- **Status:** Approved design (pre-implementation) — amended after external review
- **Repos in scope:** `microsoft/fhir-server` **only** (no shared-components change required)
- **Deferred (separate subsession, REQUIRED companion):** `fhir-paas` probe + middleware configuration
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
  startup hands off to readiness at a timeout backstop instead of failing forever.
- Keep CMK-failed pods routable so customers receive their 403 `OperationOutcome`
  (CMK routability lives on the readiness data-store check, not the startup gate).
- Keep `/health/check`'s **steady-state** contract unchanged; relocate the
  startup-window gating semantics onto `/health/startup`.
- Revert PR #5669's Degraded-tier logic on the startup check.

## Non-Goals

- Changing `fhir-paas` probe configuration or middleware allowlists in this
  session. That is a **required** companion change (see "Deferred") handled in a
  later subsession once this work merges upstream.
- Introducing health-check response caching for the new endpoints.
- Altering CMK detection semantics in the data-store checks themselves.
- Any change to `healthcare-shared-components` (an earlier draft added a tag
  there; the amended design removes that dependency entirely).

## Design Overview

Four routes are mapped in
`FhirServerApplicationBuilderExtensions.UseFhirServer`, each filtered by a
health-check **tag predicate** and given an explicit `ResultStatusCodes` map.
Check-to-probe membership is declared at registration time via tags, following
the existing `datastore:sqlServer` tag convention already present in the consumed
healthcare-shared-components package.

Two new fhir-server-owned tags are introduced: `probe:startup` and
`probe:readiness`. The readiness predicate additionally reuses the **existing**
shared `datastore:sqlServer` tag so that the SQL data-store check — registered
inside the shared package — is selected **without modifying the shared repo**.

### Endpoints

| Route | Checks (tag predicate) | HTTP mapping | Purpose |
|---|---|---|---|
| `/health/check` | caller predicate **AND NOT** `probe:startup` | Healthy/Degraded → 200, Unhealthy → 503 | Diagnostic endpoint — steady-state unchanged |
| `/health/startup` | `probe:startup` → `StorageInitializedHealthCheck` only | Healthy → 200, Unhealthy → 503 | Gate: 503 while initializing; 200 once init done or timeout backstop |
| `/health/ready` | `datastore:sqlServer` **OR** `probe:readiness` → `DataStoreHealthCheck` + `BehaviorHealthCheck` | Healthy/Degraded → 200, Unhealthy → 503 | Routing decision; CMK `Degraded` stays routable |
| `/health/live` | none (predicate → `false`) | always 200 (no checks → Healthy) | Dependency-free HTTP liveness |

New route constants in `KnownRoutes.cs`: `HealthCheckStartup` (`/health/startup`),
`HealthCheckReady` (`/health/ready`), `HealthCheckLive` (`/health/live`).

The `UseFhirServer` signature is unchanged: the startup/readiness/liveness
predicates are internal constants; only `/health/check` keeps the caller-supplied
`healthCheckOptionsPredicate` (now **AND**-combined with `NOT probe:startup`).

### Tag constants and predicates

Introduce a single `HealthCheckTags` static class (fhir-server) so tag strings are
never duplicated as inline literals (a typo silently fails open — see Edge Cases):

- `HealthCheckTags.ProbeStartup = "probe:startup"`
- `HealthCheckTags.ProbeReadiness = "probe:readiness"`
- `HealthCheckTags.DataStoreSqlServer = "datastore:sqlServer"` — mirrors the shared
  package's tag string; a startup assertion (below) fails loudly if the shared
  value ever drifts.

Predicates:

- Startup: `reg => reg.Tags.Contains(ProbeStartup)`
- Readiness: `reg => reg.Tags.Contains(DataStoreSqlServer) || reg.Tags.Contains(ProbeReadiness)`
- `/health/check`: `reg => (caller?.Invoke(reg) ?? true) && !reg.Tags.Contains(ProbeStartup)`
- Liveness: `_ => false`

Only one data store is active per deployment (SQL **or** Cosmos), so the readiness
predicate resolves to exactly one `DataStoreHealthCheck` plus the behavior check.

### Startup gate: `StorageInitializedHealthCheck`

The check becomes a **pure** startup gate. It returns only **Healthy** (init done
or timeout backstop) or **Unhealthy** (still initializing) — never Degraded, and
it makes **no CMK / Key Vault call at all**. Dropping CMK from the gate removes an
entire class of bugs found in review: a synchronous `.GetAwaiter().GetResult()`
over `IDatabaseStatusReporter` could block a request thread on an unpopulated CMK
`ValueCache` and delay the fail-open backstop. CMK routability is preserved on the
readiness route instead (a CMK-broken pod simply waits out the timeout, then
becomes routable and serves 403 `OperationOutcome`s).

Configuration (`StorageInitializedHealthCheckConfiguration`, bound from
`HealthChecks:StorageInitialization`):

- `StorageInitializationTimeout` — default **5 minutes**. Absolute backstop; after
  this the gate hands off (Healthy) regardless of state. This is the only knob;
  PR #5669's `StartupDegradedDelay` is **removed**.
- Validation registered with `.Validate(...).ValidateOnStart()` (not constructor-
  only, which fired late): timeout must be non-negative.

State machine in `CheckHealthAsync` (uses the existing `Clock` abstraction and the
`SearchParametersInitializedNotification` handler that sets `_storageReady`):

1. `_storageReady` → **Healthy** ("Successfully initialized.").
2. `waited ≥ StorageInitializationTimeout` → **Healthy** ("startup timeout
   elapsed, handing off to readiness").
3. otherwise → **Unhealthy** ("Storage is initializing. Waited: Ns.").

`_storageReady` is declared `volatile` (written on the notification thread, read
on probe threads; a single boolean flip needs only a memory barrier).

Net behavior: `/health/startup` returns 503 only while genuinely initializing, and
flips to 200 on init success or the timeout backstop. The pod is never
crash-looped by the startup probe; once startup succeeds, Kubernetes stops calling
it and readiness governs routing.

**Timeout / K8s-budget invariant.** The application timeout and the Kubernetes
startup-probe budget must satisfy:

```
legit-init-p99  <  StorageInitializationTimeout  <  k8s-startup-budget
```

Today all three are ~5 minutes (RBAC-sync p99 ≈ 5 min; app timeout 5 min; startup
budget `failureThreshold 6×5 × period 10s = 300s`), so the app flips Healthy at
the same instant Kubernetes exhausts its budget and the fail-open is never
observed. The app timeout stays at **5 minutes** (aligned to legit init p99); the
Kubernetes budget **must** be raised above it (see Deferred). This spec documents
the invariant; fhir-server cannot enforce the K8s side.

### Readiness

`/health/ready` mirrors the current `/health/check` mapping: `Healthy`/`Degraded`
→ 200, `Unhealthy` → 503. A CMK problem surfaces as `Degraded` from
`DataStoreHealthCheck` (verified: `SqlServerHealthCheck` returns `Degraded` for CMK
and DB-state errors, `Unhealthy` only for SQL transport errors), so the pod stays
routable (200) and clients receive their 403 `OperationOutcome`. Only a genuine
`Unhealthy` result pulls the pod from rotation.

Readiness deliberately does **not** include the storage-init gate. The startup
probe already gates the common case (readiness probes do not run until startup
succeeds, and startup succeeds only on `_storageReady` or the timeout backstop).
The one residual case — storage failed to initialize for a **non-CMK** reason for
longer than the timeout while the DB stays reachable — is an abnormal, alerting
pod that receives traffic and returns 500s; this matches today's single-endpoint
behavior and is out of scope to gate further.

### Liveness

`/health/live` runs zero checks (predicate returns `false`), so the report is
empty and resolves to `Healthy` → HTTP 200 whenever the request reaches the health
middleware. This is **dependency-free HTTP liveness**, not an unconditional
process-alive signal: upstream middleware (suspended-account, private-link) can
still short-circuit with 403/500. `fhir-paas` may later choose a `tcpSocket` probe
to bypass the application pipeline entirely.

## Tagging and Registration Changes (fhir-server only)

Tag convention: `HealthCheckTags` constants (above). Liveness uses no tag.

| Check | File | Change |
|---|---|---|
| `StorageInitializedHealthCheck` | `Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` (~206) | add tag `probe:startup` |
| `ImproperBehaviorHealthCheck` (`BehaviorHealthCheck`) | `Microsoft.Health.Fhir.Shared.Api/Modules/FhirModule.cs` (~196) | add tag `probe:readiness` |
| `CosmosDbHealthCheck` (`DataStoreHealthCheck`) | `Microsoft.Health.Fhir.CosmosDb/Registration/FhirServerBuilderCosmosDbRegistrationExtensions.cs` (~317) | add tag `probe:readiness` |
| SQL `DataStoreHealthCheck` | shared `SqlServerApiRegistrationExtensions.cs` | **no change** — selected via its pre-existing `datastore:sqlServer` tag |

The parallel `.AsService<IHealthCheck>()` DI registrations are untouched;
ASP.NET's `HealthCheckService` executes only the `AddCheck` registrations, so no
check runs twice (verified in review).

### Startup registration assertion

Because an empty tag selection resolves to `Healthy` → 200 (a typo or a shared-side
tag rename would silently fail open), add a fail-fast startup validation (hosted
service or invoked in `UseFhirServer`) asserting:

- the readiness predicate resolves **exactly one** registration named
  `DataStoreHealthCheck`, and
- the startup predicate resolves **exactly one** registration.

A zero/duplicate count throws at startup rather than serving a false 200.

### Thread-safety fixes

- `StorageInitializedHealthCheck._storageReady` → `volatile bool`.
- `ImproperBehaviorHealthCheck` currently mutates two coupled fields
  (`_isHealthy`, `_message`) with no synchronization. Replace them with a single
  `volatile` reference to an immutable state record so the health flag and message
  are published together atomically.

## Response Writer

The JSON response writer is currently inlined once in the single `MapHealthChecks`
call. Extract it to a private `WriteHealthReportAsync(HttpContext, HealthReport)`
helper in `FhirServerApplicationBuilderExtensions` and reuse it across all four
routes. The body shape for `/health/check` is unchanged (`overallStatus` +
per-entry `name`/`status`/`description`/`data`).

## Error Handling and Edge Cases

- **Terminal CMK failure:** the startup gate ignores CMK entirely and hands off at
  the timeout backstop; the pod becomes routable and readiness returns `Degraded`
  → 200 so clients receive their 403 `OperationOutcome`.
- **CMK failure then recovery before init completes:** the readiness data-store
  check reflects live state — `Degraded` → 200 while CMK is broken, and it can
  return `Healthy` → 200 once the DB is reachable again. (This corrects the prior
  draft's claim that readiness stayed protected here; it does not, and that is
  acceptable because a reachable DB is a routable pod.)
- **Init completes after handoff:** `_storageReady` becomes true; all checks report
  Healthy. No special handling needed.
- **Empty/typo'd tag selection:** guarded by the startup registration assertion,
  which fails fast instead of serving a false 200.
- **Concurrent probe execution + notifications:** `_storageReady` (volatile) and
  the `ImproperBehaviorHealthCheck` immutable-state swap make cross-thread reads
  safe; covered by concurrent tests below.
- **Cosmos CMK:** `CosmosDbStatusReporter` is a stub (always healthy, has a TODO).
  This no longer matters for startup because the gate makes no CMK call; Cosmos CMK
  routability, when implemented, lives on the readiness data-store check.

## Testing

Unit tests — `StorageInitializedHealthCheck` (existing test file, fake `Clock`
with millisecond-scale configuration):

- `_storageReady` → Healthy immediately.
- Not ready, `waited < timeout` → Unhealthy.
- Not ready, `waited ≥ timeout` → Healthy (backstop handoff).
- No `IDatabaseStatusReporter` / CMK interaction is exercised (the dependency is
  removed from the gate).
- Config validation via `ValidateOnStart`: negative timeout fails startup; zero and
  exact-boundary values covered.
- Concurrency: notification handler flips `_storageReady` while probes read it
  (no torn reads / lost handoff).

`ImproperBehaviorHealthCheck` tests: concurrent notification + probe reads observe a
consistent `(isHealthy, message)` pair.

Registration tests (**mandatory**, not optional):

- SQL registration path resolves exactly one readiness `DataStoreHealthCheck` via
  the `datastore:sqlServer` tag.
- Cosmos registration path resolves exactly one readiness `DataStoreHealthCheck`
  via the `probe:readiness` tag.
- The startup assertion throws when the data-store tag is missing/duplicated
  (package-skew / rename detection).

Endpoint/routing tests (fhir-server, `TestServer` / `WebApplicationFactory`):

- `/health/startup` → 503 while initializing, 200 after `_storageReady`, 200 after
  timeout backstop.
- `/health/ready` → 200 on a CMK-`Degraded` data store, 503 on `Unhealthy`.
- `/health/live` → 200 with zero checks.
- `/health/check` → does **not** include the startup gate (still 200 when DB
  reachable during the startup window; steady-state body unchanged).
- Probe-budget documentation test: assert the default `StorageInitializationTimeout`
  matches the documented invariant value.

Deferred to the fhir-paas subsession (see below): suspended-account and
private-link middleware tests for all four health paths, and "liveness reaches 200
through the production middleware pipeline."

## Configuration Change

This is a **property/API rename**, not a deployment-config migration — no
`HealthChecks:StorageInitialization` values were found in the fhir-server or
fhir-paas trees, so defaults apply. Remove PR #5669's `StartupDegradedDelay`; keep
`StorageInitializationTimeout` (default 5 min) as the single knob.

## Deferred: fhir-paas Probe + Middleware Configuration (REQUIRED companion)

Handled in a later subsession after this work merges upstream. This is **not**
optional cosmetic repointing: the new endpoints are unusable in fhir-paas until the
middleware allowlists accept them (suspended pods would otherwise 403 the new
probes and crash-loop). The companion change is an atomic set:

1. **Probes** in `fhiroperator/controllers/fhir_controller.go`: create **three
   distinct** `ExecAction` instances (today all probes share one pointer):
   - Startup → `/health/startup`, with `failureThreshold × periodSeconds`
     **strictly greater** than `StorageInitializationTimeout` plus one probe
     timeout plus scheduling margin (e.g. `6×7 = 42 × 10s ≈ 7:00` vs the 5-min app
     timeout).
   - Readiness → `/health/ready`.
   - Liveness → `/health/live` (or a `tcpSocket` probe).
2. **Suspended-account allowlist** (`SuspendedAccountMiddleware`): currently allows
   only paths ending `/health/check`; must allow all four health paths.
3. **Private-link skip paths** (`PrivateLinkValidationSettings__SkipValidationPaths`
   env in `fhir_controller.go`, and the common private-link middleware): add the
   three new paths.
4. **Ingress suspension rule** (nginx `server-snippet` `^/(?!health/check)`): widen
   to permit the new health paths (external-facing; lower priority since in-pod
   exec probes bypass ingress, but keep consistent).
5. **Provisioning defaults + middleware tests** for suspended / private-link
   accounts across all four health paths.

## Rollout Considerations

- fhir-server change is self-contained: no `healthcare-shared-components` change and
  no package-version bump are required (readiness reuses the already-shipped
  `datastore:sqlServer` tag). fhir-server can merge and release independently.
- While the new endpoints exist but `fhir-paas` still points every probe at
  `/health/check`, behavior is unchanged — the new routes are additive and unused
  until the fhir-paas companion lands.
- The fhir-paas companion must ship as one atomic change (probes + middleware
  allowlists together) to avoid crash-looping suspended pods, and must raise the
  startup budget above the 5-minute app timeout per the invariant.
