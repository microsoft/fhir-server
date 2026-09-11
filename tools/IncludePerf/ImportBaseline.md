# Import baseline tooling

`Invoke-ImportBaseline.ps1` is a narrowly scoped, **opt-in** tool for collecting comparable
wall-clock `$import` evidence while evaluating an Import or FhirPath provider configuration. It
does not change the FHIR server, create resources, reset a database, provision cloud resources, or
run `PerfTester`.

This tooling records reproducible evidence; it does **not** establish an import performance gain.
Do not use its output to claim an improvement without an operator-reviewed, comparable live run.

## Safe default: write an offline plan

The command has no live default. Without `-Execute`, it reads only the local manifest and writes a
`Planned` report. This verifies the report metadata and makes missing configuration evidence visible
before an operator sends an import request.

```powershell
.\tools\IncludePerf\Invoke-ImportBaseline.ps1 `
    -ManifestPath C:\perfdata\manifest.json `
    -OutputPath C:\perfdata\firely-plan.json `
    -FhirVersion R4 `
    -BinaryIdentity sha256:<exact-image-digest> `
    -ImportProvider Firely `
    -ImportProviderEvidenceSource 'deployment configuration snapshot identifier' `
    -FhirPathProvider Firely `
    -FhirPathProviderEvidenceSource 'deployment configuration snapshot identifier' `
    -Backend SqlServer `
    -EnvironmentIdentity isolated-import-baseline `
    -LoadProfile single-client `
    -PreparedStoreIdentity corpus-v1-fresh-store `
    -Repetitions 1
```

`BinaryIdentity` is the exact container digest or binary identity that will be used. The evidence
source fields must identify how the **effective** Import and FhirPath provider settings were
verified. A provider label without evidence is not a valid report.

The plan includes the manifest file SHA-256, manifest identity, input-file and per-resource-type
counts, FHIR version, binary identity, complete provider evidence, backend/environment/load
identity, prepared-store identity, repetitions, expected resource count, and terminal outcome.
Server CPU, allocation rate, and peak working set are explicitly reported as `unavailable`; they
are never represented as zero or as a passing server-metrics gate.

## Operator prerequisites for a live run

Live execution is intentionally explicit and is only appropriate for an operator-supplied isolated
environment. Each report represents exactly one import attempt (`Repetitions: 1`); prepare a fresh
equivalent store and invoke the tool again for every additional repetition.

1. Prepare an isolated store from the same corpus for every side and repetition. The tool never
   clears, creates, or resets a store. Record the equivalence in `PreparedStoreIdentity`.
2. Use the same binary/image identity, corpus manifest hash and counts, FHIR version, backend,
   environment/load profile, prepared-store identity, and repetition count on both sides.
3. Change exactly one selected provider capability (`Import` or `FhirPath`). Capture the effective
   provider configuration from a trusted deployment/configuration source for each run.
4. Supply a distinct operator-created error container name for every import request. `$import`
   jobs are deduplicated by request definition; a reused completed job is not evidence of a new
   baseline. Do not treat a previous status resource as a new measurement.
5. Do not include secrets, SAS query strings, tokens, or customer resource bodies in identities,
   evidence-source strings, manifests, or report paths.

The live path requires `-Execute`, a HTTPS endpoint, a HTTPS storage base URI without a query
string, and an error-container name:

```powershell
.\tools\IncludePerf\Invoke-ImportBaseline.ps1 `
    -Execute `
    -ManifestPath C:\perfdata\manifest.json `
    -OutputPath C:\perfdata\firely-run-1.json `
    -FhirVersion R4 `
    -BinaryIdentity sha256:<exact-image-digest> `
    -ImportProvider Firely `
    -ImportProviderEvidenceSource 'deployment configuration snapshot identifier' `
    -FhirPathProvider Firely `
    -FhirPathProviderEvidenceSource 'deployment configuration snapshot identifier' `
    -Backend SqlServer `
    -EnvironmentIdentity isolated-import-baseline `
    -LoadProfile single-client `
    -PreparedStoreIdentity corpus-v1-fresh-store `
    -Repetitions 1 `
    -Endpoint https://<isolated-service> `
    -StorageBaseUri https://<storage-account>.blob.core.windows.net/<container> `
    -ErrorContainerName <operator-created-unique-name> `
    -PollSeconds 10 `
    -TimeoutMinutes 60
```

Pass `-AccessToken` only when the isolated endpoint requires it. The script does not print tokens,
SAS query strings, request bodies, response bodies, or error-container contents. It disables
automatic redirects and permits authorization forwarding only to status/redirect URLs with the
same scheme, host, and port as the supplied endpoint.

Wall-clock time starts immediately before `$import` request submission and ends only at a terminal
status response. A `202` is not terminal. Polling is bounded by `TimeoutMinutes`; cancellation,
unexpected status, cross-origin redirect, request failure, count mismatch, or any per-resource
error produces a non-successful report. A `200` response with import errors is `Failed`, not a
successful benchmark.

## Comparing completed reports

Compare only reports from independently prepared but equivalent stores:

```powershell
.\tools\IncludePerf\Invoke-ImportBaseline.ps1 `
    -BaselineReportPath C:\perfdata\firely-run-1.json `
    -CandidateReportPath C:\perfdata\ignixa-run-1.json `
    -SelectedCapability Import
```

Comparison first validates both report schemas, including explicit server-metric availability. It
rejects reports that have a missing provider or binary identity/evidence source, non-successful
outcome, different corpus identity/hash/counts, FHIR version, image/binary, backend, environment,
load, prepared-store identity, or repetition count. The non-selected provider capability must also
match and the selected effective provider must actually differ.

The comparison output retains the original raw wall-clock and resource count values; it neither
rounds the stored results nor computes or labels a speedup.

## Credential-free verification

Run the local report/comparison and offline-plan tests without a FHIR endpoint:

```powershell
& .\tools\IncludePerf\tests\Test-ImportBaseline.ps1
```

The test covers successful schema validation and explicit unavailable metrics, missing provider and
binary evidence, error and resource-count failure handling, timeout comparison rejection,
incomparable corpus/image/provider reports, same-origin status validation, and the offline default.
