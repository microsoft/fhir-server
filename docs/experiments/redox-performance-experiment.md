# REDox performance experiment design

## Status and objective

Experiment design approved on October 4, 2026. Implementation, deployment, and
measurement runs are not started or authorized by this document.

Determine whether REDox reduces application CPU or resident memory for the same
correct FHIR work. Scope is FHIR R4 with SQL Server, incremental `$import`, and
API calls. There are no initial-load performance runs.

Operators need resource savings without slower or incorrect requests.
Maintainers need a repeatable comparison that isolates the JSON engine from
changes to the FHIR object model, indexing, and storage.

No performance improvement is established yet. The result may be a useful win
for one operation, no material change, a regression, or an inconclusive result.

## Baseline and evidence

Pin the baseline to FHIR Server commit
`87c599ab28f6146a5544146cca2c3543b37e2a3e`, using its default Firely provider.
Keep the same .NET runtime and Release configuration for every variant.

The current application does not have one interchangeable JSON serializer:

- [API input][input] feeds a Newtonsoft `JsonTextReader` into Firely's
  `FhirJsonParser`. The formatter has additional parsing policy for bundles.
- [Firely import parsing][import-parser] interprets each resource, applies import
  metadata and reference rules, and creates a resource wrapper.
  [Wrapper creation][wrapper] serializes the resource through
  [RawResourceFactory][raw-factory] and extracts search indexes.
- [API output][output] selects between raw responses and Firely serialization.
  [Raw response serialization][raw-output] copies stored JSON when metadata is
  already set and pretty printing is off. Other raw responses use
  System.Text.Json for metadata updates or formatting. `_summary` and
  `_elements` can cause Firely object materialization.
- [The import loader][loader] reads NDJSON lines before invoking the selected
  parser. Keep its buffering, partition boundaries, and line handling unchanged
  so the experiment does not change two variables.
- [The existing Ignixa migration][ignixa-adr] changes more than a JSON tokenizer.
  Do not switch providers during this experiment or attribute an SDK migration
  result to REDox.

[REDox's published benchmarks][redox-readme] use non-FHIR datasets and are not
server performance evidence. Its Newtonsoft compatibility package is preview.
The reviewed [converter adapter][redox-adapter] encodes a REDox element, converts
the bytes to text, and reparses it with Newtonsoft during reads. Any such
conversion belongs inside the measured operation.

The observed public release is [v1.0.0][redox-release], at source revision
`47a9b74442e8fc3456163fd8a02db9245a111bed`. The README and adapter reviewed above
are from the later main revision `e64ee501c18356a7812d68eab0a83b915444d2fd`.
Before implementation, verify the selected package's APIs and record its exact
version, dependency graph, and corresponding source revision. Do not assume the
release package matches the reviewed main revision.

## Approach

| Approach | What it answers | Decision |
|---|---|---|
| Library-only benchmark | Does REDox improve parsing or writing for our JSON shapes? | Use for screening, not for the adoption decision. |
| Narrow integration with existing FHIR behavior | Does replacing a particular JSON operation improve the application after conversion costs? | Recommended experiment. |
| Replace the FHIR serialization stack | Could a different SDK, object model, and serializer perform better together? | Excluded. Too many variables and a larger migration. |

Use staged gates. Do not expand a failed narrow experiment into a new FHIR SDK
implementation.

## Stage 1. Baseline profile and correctness corpus

Generate synthetic R4 data with a fixed seed and a hashed manifest. Include
Patient, Observation, Encounter, DiagnosticReport, ExplanationOfBenefit, and
large narrative or attachment cases. Report typical resources separately from
large stress cases. Use no patient data.

Use the same source corpus for component measurements, incremental import, and
API calls. Record resource counts, UTF-8 byte counts, type mix, size distribution,
and the create/update mix.

Profile the baseline application before selecting a replacement. Record call
counts per resource or request, per-call duration and allocation, and application
CPU attributed to JSON parsing, FHIR materialization, serialization, search
indexing, compression, and database access. Record wall-clock I/O waits
separately. Use diagnostic profiling runs separately from primary timing runs.

Apply the [FHIR R4 JSON rules][fhir-json] and the existing formatter and import
tests. Add literal expected results as well as baseline comparisons. Cover:

- Decimal precision, significant trailing zeros, partial dates, time zones,
  Unicode, escaping, and narrative strings.
- Primitive extensions, extension-only values, aligned primitive null arrays,
  choice types, contained resources, and `resourceType` appearing last.
- Malformed input, duplicate properties, unknown fields, invalid resource IDs,
  JSON5-only syntax, nesting and size limits, and error responses.
- Import version and timestamp policy, conditional references, soft deletion,
  error counts, and per-resource error attribution.
- API status codes, headers, OperationOutcome codes and severity, bundle
  results, projection behavior, resource history, and persisted content.

Preserve the current documented parsing policy, including leniency. A mismatch
between existing behavior and the specification is a separately reported issue,
not permission to change behavior while measuring performance.

Compare search indexes using real indexers and verify representative searches.
The existing [ImportResourceParserParityTests][import-parity] substitute
`ISearchIndexer`, so those tests alone cannot establish index equivalence.

Compare FHIR semantics and array ordering. Assert numeric precision separately
so generic JSON equality cannot hide a decimal representation change. Preserve
exact output on paths whose contract is direct copying. In end-to-end tests,
normalize only explicitly identified generated IDs, timestamps, and endpoint
URLs. Assert their required values or relationships before normalization.

Both import modes use the selected parser, but initial load has distinct
metadata and conditional-reference rules. Keep mode-specific correctness tests
when changing shared parsing code. Do not add an initial-load performance run.

**Gate.** No unexplained correctness differences before performance comparisons.

## Stage 2. Component measurements and integration candidates

First investigate a REDox-token reader that feeds the same Firely FHIR
interpretation used by the baseline. This is a feasibility hypothesis, not an
adapter known to exist in this repository.

Measure the complete conversion into the required application representation.
Include token traversal, buffering, string encoding, object materialization,
serialization, and disposal. Keep any unavoidable intermediary object model in
the result. Do not present a generic `Deserialize<Resource>` benchmark as a
replacement for FHIR parsing.

Use separate candidate runs for:

- Import parsing, with the same `IImportResourceParser` result and policies.
- API input, with the same Firely models and formatter error behavior.
- A specific API output operation, only if the baseline profile justifies it.

Evaluate combined changes only after attributing results to the individual
candidates. The comparison may show that only one operation benefits.

Keep validation, authorization, SQL, search indexing, and the NDJSON loader
unchanged. Preserve the direct-copy response path. Do not add REDox as a
`FhirSdkProvider` value because it is not a FHIR SDK.

If an experiment-only selector is needed, choose the implementation at startup,
default to the baseline, and log the active replacements. Validate the unchanged
baseline build against the selector-off build before trusting that control.
Never silently fall back to Firely after a candidate fails.

Disable REDox parallel deserialization for the first comparison. If the
sequential candidate passes, evaluate parallel deserialization separately under
the same CPU budget and request or import concurrency. Lower wall time alone
does not establish lower CPU consumption.

**Gate.** A compatible candidate with measurable component benefit and a
plausible end-to-end benefit based on the baseline profile. If conversion costs
erase the benefit, preserve the evidence and stop that candidate.

## Stage 3. Application workloads

| Workload | Coverage | Primary measurements |
|---|---|---|
| Incremental `$import` | Identical seeded databases, NDJSON partitions, worker limits, and fixed creates and updates. Include job completion and finalization. | Successful resources per second and application CPU core-seconds per successful resource. |
| API writes | POST and PUT with small, typical, and large resources. Keep transaction and batch compatibility cases separate. | CPU per successful request, sustainable throughput, and p50/p95/p99 latency. |
| API reads and search | Read-by-ID, paged search bundles, metadata rewriting, `_pretty`, `_summary`, and `_elements`. | CPU per request and latency for equal result sets and resource content. |
| Unchanged operations | Direct-copy responses and operations outside each candidate's replacement. | Environmental noise and accidental overhead. |

Prepare incremental-import databases outside the timed workload using one fixed
procedure. Initial import is not a benchmark or a required setup operation.
Each measured run starts from equivalent resource versions, indexes, and job
state so import request deduplication cannot turn a repeat into a no-op.

For every workload, also collect:

- Total allocated bytes and allocated bytes per successful operation.
- GC collection counts, pause time, managed heap size, and large-object heap
  behavior.
- Peak and steady working set or RSS, plus private bytes where available.
- Error, timeout, retry, and successful-operation counts.
- SQL CPU, waits, I/O, and blob and network throughput, reported separately
  from application measurements.

Application CPU totals include all server and background import-worker
processes involved in the run. CPU core-seconds per successful operation is
their summed CPU-time increase divided by the verified successful count.
Measure the full interval through completion, including queued work draining.

Fewer allocated bytes do not establish lower resident memory. A pool can reduce
allocations while retaining more memory. Include a sustained run to identify
retained buffers and memory growth.

## Fair comparison and analysis

1. Run an A/A calibration with unchanged code to establish environmental noise.
2. Run a baseline-only pilot to choose corpus size, offered load, warm-up,
   measurement duration, and concurrency. Freeze these before observing
   candidate results.
3. Pin runtime, Release configuration, CPU and memory limits, GC settings, SQL
   schema and statistics, authentication, logging, compression, and dependencies.
4. Use equivalent isolated database starting states for writes and import.
   Never compare variants against different accumulated update histories.
5. Run variants sequentially on the same reserved compute. Use a separate load
   generator and avoid concurrent application or SQL benchmark traffic.
6. Predeclare at least five independent paired runs. Balance and randomize A/B
   order, restart application processes, and warm each equally. Keep cold-start
   and steady-state results separate.
7. Compare API resource efficiency at matched offered load and successful
   throughput. Use a scheduled load generator that records queue delay and
   timeouts, rather than hiding latency through coordinated omission. Measure
   saturation throughput separately.
8. Calculate paired effect ratios and 95% confidence intervals using
   independent runs as the sampling unit. Do not treat correlated requests
   within a run as independent trials. Do not average percentile values and
   label the result a pooled percentile.

Freeze a primary workload and analysis method per candidate before its A/B
runs. Preserve all valid run results. Define invalid-run criteria in advance,
such as an unrelated host restart or a failed prerequisite. Do not discard runs
because the candidate performed poorly.

Use identical observation overhead for both variants. Confirm the predicted
changed code actually runs with a separate trace or diagnostic counter.

## Proposed acceptance criteria

These are approved experiment targets, not measured results.

A candidate earns further work if a representative end-to-end workload has
either:

- At least 10% lower application CPU core-seconds per successful operation.
- At least 15% lower steady resident memory at matched workload and goodput.

Correctness must remain unchanged. Throughput must not fall by more than 5%.
p95 and p99 latency, peak memory, and the other resource metric must not increase
by more than 5%. For import, also compare total completion time and throughput.
Compare each metric against the same baseline workload, not against another
request or resource mix.

Confidence intervals must support the benefit threshold and the regression
limits, not just the point estimates. For example, the CPU-benefit ratio's upper
95% confidence bound must be at most 0.90. Report insufficient precision as
inconclusive rather than silently increasing the trial count until a run passes.

Report each workload separately. An output-only improvement does not establish
an import improvement. Unchanged-path control movement that could explain the
apparent improvement invalidates the conclusion.

A failed gate stops that candidate. An experiment pass authorizes a
recommendation for further work, not production adoption or merging a new
serializer.

## Reuse and deliverables

Reuse the deterministic generator and request construction in
[`tools\IncludePerf`][include-perf] where they fit the corpus. Extend only the
missing measurements and workloads. The current
[`Invoke-BulkImport.ps1`][import-script] defaults to polling every 60 seconds.
Use server job timestamps for performance timing, not the polling observation
time.

[`tools\ABTestRunner`][ab-runner] provides correctness-oriented setup, disables
authorization, and compares E2E test durations. It is not sufficient evidence
of production-like API latency or resource savings. Pin baseline images by
digest rather than using its moving `master` tag.

Preserve a rerunnable runner, corpus manifest, configuration and dependency
snapshot, commit or image identities, raw samples, latency histograms, GC and
process counters, diagnostic traces, and paired result summaries. Record
candidate selection and correctness counts with every run. Keep credentials,
access tokens, connection strings, and request bodies out of public artifacts.

The report states pass, regression, no material improvement, or inconclusive
for each candidate and workload. It separates measured results from
interpretation and states exactly which JSON operations changed.

## Safety and exclusions

Use a named disposable test environment only after its owner approves it.
Provisioning, deployment, database resets, and cleanup require approval.
Do not use production or shared CI infrastructure.

Exclude initial-load performance runs, Cosmos DB, other FHIR versions, XML,
changes to NDJSON reading, and completion of the Ignixa migration. Keep existing
correctness coverage for shared code that a candidate changes.

No performance scripts, dependencies, server changes, or benchmark results are
part of this design-only commit.

## Design principles

The Laziness Protocol keeps replacements narrow and reuses existing tooling.
Prove It Works makes correct end-to-end FHIR work and actual application
resource use the decision criteria. A faster library benchmark is not enough.

The throughput checkpoint for this read-only investigation is not applicable.
The implementation and measurement schedule belongs to a separately approved
plan.

[input]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Shared.Api/Features/Formatters/FhirJsonInputFormatter.cs
[import-parser]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.FirelySdk/Features/Operations/Import/FirelyImportResourceParser.cs
[wrapper]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Core/Features/Persistence/ResourceWrapperFactory.cs
[raw-factory]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Shared.Core/Features/Persistence/RawResourceFactory.cs
[output]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Shared.Api/Features/Formatters/FhirJsonOutputFormatter.cs
[raw-output]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Core/Extensions/RawResourceElementExtensions.cs
[loader]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Core/Features/Operations/Import/ImportResourceLoader.cs
[ignixa-adr]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/docs/arch/adr-2607-ignixa-import-phase0.md
[redox-readme]: https://github.com/CAPCOM-TD-OSS/REDox/blob/e64ee501c18356a7812d68eab0a83b915444d2fd/README.md
[redox-adapter]: https://github.com/CAPCOM-TD-OSS/REDox/blob/e64ee501c18356a7812d68eab0a83b915444d2fd/src/REDox.Serialization.NewtonsoftJson/JsonConverterAdapter.cs
[redox-release]: https://github.com/CAPCOM-TD-OSS/REDox/releases/tag/v1.0.0
[fhir-json]: https://hl7.org/fhir/R4/json.html
[import-parity]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Operations/Import/ImportResourceParserParityTests.cs
[include-perf]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/tools/IncludePerf/README.md
[import-script]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/tools/IncludePerf/Invoke-BulkImport.ps1
[ab-runner]: https://github.com/microsoft/fhir-server/blob/87c599ab28f6146a5544146cca2c3543b37e2a3e/tools/ABTestRunner/README.md
