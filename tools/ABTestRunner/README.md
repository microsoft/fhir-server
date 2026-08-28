# A/B Test Runner

Deploys two isolated FHIR servers to Azure Container Apps and compares either E2E
tests or purpose-built ingestion workloads.

The existing behavior remains the default:

- **Baseline:** latest `master` image from CI.
- **Branch:** an image built from the current checkout.
- **Workload:** E2E tests, run in parallel as before.

`-ComparisonMode SameImageProvider` instead deploys the current checkout image on
both sides. It explicitly configures all three SDK seams. Its default is an
isolated FHIRPath comparison:

| Side | Default | Import | FhirPath |
|---|---|---|---|
| Control | Firely | Firely | Firely |
| Treatment | Firely | Firely | Ignixa |

## Prerequisites

- PowerShell 7, Azure CLI, Docker with buildx, and the repository .NET SDK.
- An authenticated Azure CLI session with permission to create resource groups,
  Container Apps, SQL resources, identities, and role assignments.
- Access to `healthplatformregistry.azurecr.io` (or `-ContainerRegistry`).
- User Access Administrator (or equivalent role-assignment permission).
- For `$import`, an existing Azure Storage account for integration output and
  one or more NDJSON blobs in that same account. Every input URL must have the
  same scheme and IDN host as `-ImportStorageAccountUri` and must not contain a
  query string, matching the server import contract. The runner enables a
  system-assigned identity on each Container App and grants each identity
  `Storage Blob Data Contributor` on `-ImportStorageAccountResourceId`; the
  caller must be allowed to create those assignments.

## Existing E2E comparison

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -FhirVersion R4 `
    -DataStore SqlServer `
    -Subscription "your-subscription-name-or-id" `
    -ResourceGroupPrefix "abtest"
```

Existing defaults, image selection, test filters, replica count, and E2E
parallel execution are unchanged. TRX duration comparison is useful regression
evidence, but it is **not a load-test gate**: E2E tests perform heterogeneous
setup and assertions, do not hold request concurrency constant, and report
whole-test duration rather than isolated ingestion throughput.

## Bundle ingestion: FHIRPath-only comparison

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Bundle `
    -FhirVersion R4 `
    -DataStore SqlServer `
    -Subscription "your-subscription-name-or-id" `
    -ResourceGroupPrefix "fhirpath-bundle" `
    -BundleCount 100 `
    -BundleSize 100 `
    -Concurrency 8 `
    -WarmupIterations 1 `
    -MeasuredIterations 5
```

The bundle workload creates the same deterministic transaction bundles on both
sides. It records successful resources, failures, elapsed time, resources/sec,
and request p50/p95/p99. A direct read and an indexed family-name search gate
each iteration.

## `$import`: FHIRPath-only comparison

`Import=Firely` on both sides is the default in same-image mode.

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Import `
    -FhirVersion R4 `
    -DataStore SqlServer `
    -Subscription "your-subscription-name-or-id" `
    -ResourceGroupPrefix "fhirpath-import" `
    -ImportInputUrl "https://fhirperftest.blob.core.windows.net/input/patients.ndjson" `
    -ImportResourceType Patient `
    -ImportSearchProbe "Patient?identifier=https%3A%2F%2Fexample.org%2Fab-perf%7Cmeasured-0001" `
    -ImportExpectedResourceCount 100000 `
    -ImportStorageAccountUri "https://fhirperftest.blob.core.windows.net/" `
    -ImportStorageAccountResourceId "/subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>" `
    -MeasuredIterations 1
```

The runner submits a FHIR `Parameters` request to `$import`, polls its status URL
immediately and then at a configurable short interval (`-ImportPollIntervalSeconds`,
default 1 second), and records imported resources, failures, total time, and
resources/sec. The same URL manifest is submitted to each isolated database.
`-ImportSearchProbe` is required and must provide one deterministic indexed
query per distinct imported type in alphabetical resource-type order; type-only
and `_count`-only probes are rejected. Each probe must return at least one
resource of its mapped type. Any
terminal `error` entry (even without a count), count mismatch, HTTP error, or
failed indexed probe fails the run.

Import warm-up defaults to zero because a useful warm-up must perform a real
import. To request warm-up iterations, provide a separate complete corpus:

```powershell
    -WarmupIterations 1 `
    -ImportWarmupInputUrl "https://fhirperftest.blob.core.windows.net/input/warmup-patients.ndjson" `
    -ImportWarmupResourceType Patient `
    -ImportWarmupSearchProbe "Patient?identifier=https%3A%2F%2Fexample.org%2Fab-perf%7Cwarmup-0001" `
    -ImportWarmupExpectedResourceCount 1000
```

Warm-up imports pass the same job, error, count, and indexed-search gates but
are excluded from measured results. Warm-up and measured inputs must not
collide. Use isolated/replay-safe resources for every repeated warm-up or
measured import, or start each experiment with clean databases.

## Combined Ignixa import parser and FHIRPath

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Import `
    -TreatmentImportProvider Ignixa `
    -TreatmentFhirPathProvider Ignixa `
    -Subscription "your-subscription-name-or-id" `
    -ResourceGroupPrefix "ignixa-combined" `
    -ImportInputUrl "https://fhirperftest.blob.core.windows.net/input/patients.ndjson" `
    -ImportResourceType Patient `
    -ImportSearchProbe "Patient?identifier=https%3A%2F%2Fexample.org%2Fab-perf%7Cmeasured-0001" `
    -ImportExpectedResourceCount 100000 `
    -ImportStorageAccountUri "https://fhirperftest.blob.core.windows.net/" `
    -ImportStorageAccountResourceId "/subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>"
```

## Dry-run validation

Dry-run performs parameter compatibility and import host/probe checks and emits
the complete deployment/workload plan without invoking Docker or Azure. The
plan includes each Container App's `--system-assigned` create command,
system-principal lookup, per-app storage-role assignment, and propagation wait:

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Bundle `
    -Subscription unused-in-dry-run `
    -ResourceGroupPrefix dryrun `
    -DryRun `
    -ValidateCleanupOnFailure `
    -PlanOutputPath ./ab-plan.json
```

`-ValidateCleanupOnFailure` uses mock cleanup commands, forces a workload-style
failure, verifies that both cleanup actions run, and verifies that the original
failure remains the reported error. It never calls Docker or Azure.

Provider overrides are rejected in the default baseline-image mode. Import
inputs are rejected for other workloads, and import mode requires an input URL,
matching resource types, indexed probes, expected count, and integration
storage details. Import host mismatch and incomplete warm-up corpora are
rejected before any Azure operation.

Run all credential-free planning matrices and local HTTP mocks with:

```powershell
./tools/ABTestRunner/Test-ABTestRunner.ps1
```

The mock suite covers FHIR JSON transaction bundles, direct and indexed gates,
useful failed-request errors, relative duplicate import status headers, terminal
success and error-without-count responses, indexed import probe success/failure,
and separate real warm-up and measured imports.

## Methodology

- Each side has a separate database and fixed two-replica Container App.
- Control and treatment run sequentially by default to reduce shared
  infrastructure interference. `-ParallelWorkloads` is an explicit opt-in.
- Use the same corpus, region, database tier, and input blobs on both sides.
- Use warm-up iterations to reduce startup effects and multiple measured
  iterations to expose variance.
- Do not reuse databases between experiments; allow cleanup or manually reset
  all data.
- During production bake-in, monitor failure telemetry whose operation name is
  `FhirPathSearchIndexEvaluation`. The runner does not automate this because it
  has no Application Insights/telemetry workspace access.

## Output

Results are written to `./ab-test-results/<timestamp>`.
Every output directory contains `run-metadata.json` with stable comparison mode,
image, provider, workload, and parameter fields. URI query strings are omitted
so credentials such as SAS tokens cannot be persisted. E2E Markdown reports
also contain image/provider provenance while E2E CSV column labels remain the
legacy `Baseline_*` and `Branch_*` names.

E2E mode produces TRX files, `comparison-report.md`, and
`detailed-results.csv`. Ingestion mode produces:

- `ingestion-results.json` — machine-readable per-iteration results.
- `ingestion-results.csv` — tabular per-iteration results.
- `ingestion-comparison.md` — concise aggregate and percentage comparison.
