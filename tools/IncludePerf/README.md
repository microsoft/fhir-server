# `_include` / `_revinclude` performance A/B harness

Tooling to prove whether a change to include handling — specifically
[PR #5683](https://github.com/microsoft/fhir-server/pull/5683), which enforces SMART compartment
authorization on `_include`/`_revinclude` — causes a performance regression.

It complements [`tools/ABTestRunner`](../ABTestRunner/README.md) (PR #5704). That tool answers *"do the
E2E tests still pass, and roughly how long did they take?"*. This one answers *"what happened to
`_include`/`_revinclude` latency, at realistic data volumes, under SMART scopes?"* — which the E2E
runner structurally cannot, because it deploys both services with `FhirServer__Security__Enabled=false`,
so the SMART compartment rewriter never engages.

---

## Why a separate harness

| Concern | `ABTestRunner` | This harness |
|---|---|---|
| SMART scopes exercised | No — auth disabled | Yes — dev IdP issues real SMART tokens |
| Data volume | Whatever the E2E tests create | ~3.9M resources / ~15M `ReferenceSearchParam` rows |
| What is measured | xUnit test durations (noisy) | Per-query p50/p90/p95/p99 over N iterations |
| Result-set changes | Not surfaced | Entry counts reported next to latency |
| Databases | One per service | **One shared database** |

### One shared database, on purpose

PR #5683 changes **no SQL schema** (verified: no migration files, `SchemaVersionConstants` identical), so
both images can run against the same database. That is strictly better for A/B fairness:

- identical data, statistics, index fragmentation and page layout — the usual sources of false deltas
  between two separately-imported databases disappear
- the dataset is imported once instead of twice
- non-SMART queries generate byte-identical SQL on both sides, so they share a cached plan, which is
  exactly what a control should do

The trade-off is a shared buffer pool, handled by alternating rounds (see below).

---

## Components

| Path | What it does |
|---|---|
| `FhirPerfDataGenerator/` | Generates deterministic FHIR NDJSON with heavy reference fan-out and deliberate cross-compartment links |
| `FhirIncludeBenchmark/` | Acquires tokens, runs the query catalog, reports latency percentiles; also has an NDJSON loader mode |
| `FhirPerfSqlOps/` | Runs T-SQL against Azure SQL with an Entra ID token (Entra-only auth) |
| `Invoke-BulkImport.ps1` | Builds the `$import` Parameters body from the manifest and polls to completion |
| `Invoke-IncludePerfABTest.ps1` | Orchestrates alternating benchmark rounds and produces the report |
| `Compare-IncludeBenchmark.ps1` | Joins baseline/branch results and classifies each case |
| `Start-LocalFhirServer.ps1` | Runs a local server configured for SMART benchmarking |

---

## The dataset

`--profile large` produces **3,887,425 resources (~1.9 GB)** in about a second:

- 25,000 patients; the first 25 are "heavy" with 10x the normal compartment (~1,530 resources each)
- ~154 resources per normal patient across 12 clinical types
- shared/universal resources: 2,000 Practitioner, 500 Location, 300 Medication, 200 Organization

### Reference topology

Chosen so forward includes, reverse includes and `:iterate` chains all have real fan-out:

```
Patient            -> Practitioner (general-practitioner), Organization
Encounter          -> Patient, Practitioner, Organization, Location
Observation        -> Patient, Encounter, Practitioner [, focus -> Patient] [, device -> Device]
Condition          -> Patient, Encounter, Practitioner
MedicationRequest  -> Patient, Encounter, Practitioner, Medication
DiagnosticReport   -> Patient, Encounter, Organization, Observation[] (drives _include:iterate)
DocumentReference  -> Patient, Encounter, Practitioner, Organization
Procedure          -> Patient, Encounter, Practitioner
AllergyIntolerance -> Patient, Practitioner
Immunization       -> Patient, Encounter, Practitioner, Location
CarePlan           -> Patient, Encounter, Practitioner
Device             -> Patient (60%); the rest are unassigned
```

### Cross-compartment links are essential

Without them every reference stays inside one compartment, the authorization predicate never excludes
anything, and baseline and branch return *identical* results — so the fix could not be shown to work.
`CrossCompartmentPercent` (default 20%) creates three leak shapes that mirror the PR's own integration
tests:

| # | Link | Reachable from a victim compartment via |
|---|---|---|
| 1 | Patient P's DiagnosticReport → Patient P-1's Observation | `_revinclude=Observation:subject&_revinclude:iterate=DiagnosticReport:result` |
| 2 | Patient P's Observation `focus` → Patient P+1 | `_include=DiagnosticReport:result&_include:iterate=Observation:focus` |
| 3 | Patient P's Observation `device` → Patient P+1's Device | `_include=Observation:device` (conditional Device rules) |

---

## The query catalog

Three families, so the report can separate signal from noise:

- **`admin-*`** — no SMART scopes. PR #5683 leaves this SQL untouched
  (`SqlRootExpression.SmartCompartmentMembership` is null), so these calibrate environmental noise.
- **`smart-*`** — SMART v1 patient scope. The primary subject of the comparison.
- **`smartv2-*`** — granular scopes with search parameters, taking the second, more expensive code path
  where the scope-restricted set is regenerated in a new `WITH` clause.

Each family includes a `-control-noinclude` case so the include CTE cost can be separated from the base
compartment query cost. Coverage spans forward includes, reverse includes, wildcards,
`_include:iterate`, `_revinclude:iterate`, a 3-hop iterate chain, `$includes` continuation paging, and
the Device conditional rules.

Every case runs against a **heavy** patient (worst case) and a **typical** patient.

---

## Reading the report

PR #5683 deliberately changes results as well as timing, so latency alone is misleading. The report puts
returned-entry counts next to latency and classifies each case:

| Verdict | Meaning |
|---|---|
| `REGRESSION` | Slower while returning the same or fewer entries — a real cost increase |
| `SLOWER (more data)` | Slower while returning more entries — investigate |
| `IMPROVED` | Faster with the same entry count |
| `FASTER (less data)` | Faster only because the fix removed leaked rows — **not** a genuine win |
| `UNCHANGED` | Within the noise threshold |

The **control calibration** section reports the largest p95 movement across the non-SMART cases. Treat
SMART deltas smaller than that as indistinguishable from noise.

---

## Usage

### 1. Generate data

```powershell
dotnet run --project tools/IncludePerf/FhirPerfDataGenerator -c Release -- `
    --profile large --output C:\perfdata-large --workers 8
```

Profiles: `small` (1k patients), `medium` (5k), `large` (25k). Override with `--patients N`.

### 2. Provision Azure

Both services must run with **security enabled** and the development identity provider on, because SMART
tokens are the whole point. Register one dev-IdP client application **per benchmark patient**, using the
Patient resource id as the client id — `OpenIddictAuthorizationController.CreateFhirUserClaim` derives
`fhirUser` from the client id, which is why generated patient ids contain the literal `patient`.

Required container app settings:

```
FhirServer__Security__Enabled=true
FhirServer__Security__Authorization__Enabled=true
FhirServer__Security__Authorization__ScopesClaim__0=scope
FhirServer__Security__Authentication__Authority=https://<app-fqdn>/
FhirServer__Security__Authentication__Audience=fhir-api
FhirServer__Operations__Includes__Enabled=true
DevelopmentIdentityProvider__Enabled=true
DevelopmentIdentityProvider__ClientApplications__0__Id=globalAdminServicePrincipal
DevelopmentIdentityProvider__ClientApplications__0__Roles__0=globalAdmin
DevelopmentIdentityProvider__ClientApplications__1__Id=perf-patient-000000
DevelopmentIdentityProvider__ClientApplications__1__Roles__0=smartUser
...
```

### 3. Load the data

`$import` needs, on the importing service:

```
FhirServer__Operations__Import__Enabled=true
FhirServer__Operations__Import__InitialImportMode=true
FhirServer__Operations__IntegrationDataStore__StorageAccountUri=https://<account>.blob.core.windows.net/
TaskHosting__Enabled=true
```

```powershell
./tools/IncludePerf/Invoke-BulkImport.ps1 `
    -Endpoint https://<baseline-fqdn> `
    -ManifestPath C:\perfdata-large\manifest.json `
    -StorageAccount <account> -Container ndjson `
    -Mode InitialLoad -ErrorContainerName import-errors
```

### 4. Run the A/B benchmark

Turn `InitialImportMode` **off** and security **on** for both services first.

```powershell
./tools/IncludePerf/Invoke-IncludePerfABTest.ps1 `
    -BaselineEndpoint https://<baseline-fqdn> `
    -BranchEndpoint   https://<branch-fqdn> `
    -ManifestPath     C:\perfdata-large\manifest.json `
    -Rounds 3 -Iterations 25
```

---

## Gotchas discovered the hard way

- **`InitialImportMode` blocks `POST /connect/token`.** `InitialImportLockMiddleware` rejects every
  non-GET FHIR request except `$import` with `423 Locked` — including token acquisition. Import with
  security disabled, then re-enable it for benchmarking.
- **`$import` requires a *system-assigned* managed identity.** `AzureAccessTokenClientInitializerV2`
  hard-codes `ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)`; a user-assigned identity is
  ignored. Every service with `TaskHosting__Enabled=true` can pick up import jobs, so **all** of them
  need `Storage Blob Data Contributor` — one unauthorized worker fails the entire job.
- **`$import` requests are de-duplicated by request hash.** A failed job cannot simply be resubmitted;
  vary the request (for example via `errorContainerName`) or the server returns the old failed job.
- **ACR Tasks cannot parse `FROM --platform=$BUILDPLATFORM`.** Dependency scanning fails before the
  build starts. Build from a worktree with that line pinned (and `ARG TARGETARCH=amd64`). This is the
  practical path on an ARM64 workstation, where a local amd64 build would be emulated.
- **Common subscription policies** block SQL local auth (`Entra-only` required) and storage shared-key
  access. Create SQL with `--enable-ad-only-auth` and storage with `--allow-shared-key-access false`,
  then use `--auth-mode login` for blob operations.
- **Do not run the two sides in parallel.** They share a database; concurrent runs contend and produce
  meaningless numbers.
