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
  one or more blob URLs containing NDJSON. Input URLs must be readable by the
  service (for example, SAS URLs). The runner grants its managed identity
  `Storage Blob Data Contributor` on `-ImportStorageAccountResourceId`; the
  caller must be allowed to create that assignment.

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
    -ImportInputUrl "https://data.blob.core.windows.net/input/patients.ndjson?<sas>" `
    -ImportResourceType Patient `
    -ImportExpectedResourceCount 100000 `
    -ImportStorageAccountUri "https://results.blob.core.windows.net/" `
    -ImportStorageAccountResourceId "/subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>" `
    -WarmupIterations 1 `
    -MeasuredIterations 1
```

The runner submits a FHIR `Parameters` request to `$import`, polls its status URL
to completion, and records imported resources, failures, total time, and
resources/sec. The same URL manifest is submitted to each isolated database.
Any job errors, count mismatch, or empty representative type search fails the
run. Import defaults to one measured iteration; its warm-up is a metadata
request so the measured corpus is not preloaded. Use multiple measured import
iterations only with replay-safe NDJSON, or run separate experiments with clean
databases.

## Combined Ignixa import parser and FHIRPath

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Import `
    -TreatmentImportProvider Ignixa `
    -TreatmentFhirPathProvider Ignixa `
    -Subscription "your-subscription-name-or-id" `
    -ResourceGroupPrefix "ignixa-combined" `
    -ImportInputUrl "https://data.blob.core.windows.net/input/patients.ndjson?<sas>" `
    -ImportResourceType Patient `
    -ImportExpectedResourceCount 100000 `
    -ImportStorageAccountUri "https://results.blob.core.windows.net/" `
    -ImportStorageAccountResourceId "/subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>"
```

## Dry-run validation

Dry-run performs parameter compatibility checks and emits the complete
deployment/workload plan without invoking Docker or Azure:

```powershell
./tools/ABTestRunner/Invoke-ABTest.ps1 `
    -ComparisonMode SameImageProvider `
    -Workload Bundle `
    -Subscription unused-in-dry-run `
    -ResourceGroupPrefix dryrun `
    -DryRun `
    -PlanOutputPath ./ab-plan.json
```

Provider overrides are rejected in the default baseline-image mode. Import
inputs are rejected for other workloads, and import mode requires an input URL,
matching resource types, expected count, and integration storage details.

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

E2E mode produces TRX files, `comparison-report.md`, and
`detailed-results.csv`. Ingestion mode produces:

- `ingestion-results.json` — machine-readable per-iteration results.
- `ingestion-results.csv` — tabular per-iteration results.
- `ingestion-comparison.md` — concise aggregate and percentage comparison.
