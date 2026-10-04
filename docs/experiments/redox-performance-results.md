# REDox component screening results

## Decision

**Do not integrate this prototype. No application CPU or resident-memory saving
has been demonstrated.** The input reader changes accepted input syntax.
The metadata rewrite allocates less but consumes more CPU in the isolated
component comparison.

The October 4, 2026 run completed component screening, not the full
[approved experiment](redox-performance-experiment.md).
**Incremental `$import` and HTTP API measurements were not run.**
There are no endpoint latency, SQL import throughput, or steady service-memory
results in this report. No production source files or defaults changed.

## Measured components

Each workload has five independent paired comparisons in fresh Release
processes. CPU values below are medians in microseconds per resource.
Changes are paired geometric means, not ratios of the displayed medians.
Positive changes mean more cost.

| Component | Baseline CPU | REDox CPU | Paired CPU change | 95% interval |
|---|---:|---:|---:|---:|
| Firely parse through the selected JSON reader | 270.31 | 305.47 | +8.2% | -3.0% to +23.5% |
| Same parse plus Firely serialization | 453.12 | 470.31 | +6.3% | -1.8% to +14.2% |
| Isolated metadata rewrite | 6.06 | 6.98 | +15.7% | +12.8% to +19.0% |

Input CPU results are inconclusive. The unchanged-baseline controls varied by
-15.0% and +50.6% for parsing. Their variation can explain the apparent input
timing differences. Roundtrip controls varied by -5.5% and -4.4%.
Metadata controls varied by -0.5% and +3.4%, below its observed CPU increase.
The shared host was not reserved or isolated from other workloads.

| Component | Baseline allocated bytes per resource | REDox allocated bytes per resource | Paired allocation change |
|---|---:|---:|---:|
| Firely parse | 161,624 | 166,044 | +2.7% |
| Parse plus Firely serialization | 292,403 | 296,755 | +1.5% |
| Metadata rewrite | 8,283 | 4,482 | -45.9% |

The metadata result is a tradeoff, not a resource-efficiency win.
Its whole-process peak working set increased by 7.2%.
The paired interval is +7.1% to +7.3%.
That peak includes startup and warm-up, so it is not peak request memory.
Its end-of-run working set changed by +1.2%, with an interval of -2.6% to +5.3%.
None of these snapshots establishes lower steady service memory.

Elapsed-time changes were +20.7% for parsing, +4.7% for roundtrip, and +13.0% for
metadata. These are component elapsed times, not HTTP latencies.
All raw samples, memory counters, GC counts, and intervals are preserved in the
[run artifacts](../../tools/RedoxExperiment/results/2026-10-04).

## Correctness outcome

All 1,000 corpus resources produced byte-identical Firely-reserialized output
through both readers. Metadata rewrites matched semantically across that corpus.
Literal assertions also covered Unicode, partial dates, time-zone offsets,
decimal scale, choice types, extension-only primitive values, aligned primitive
arrays, resource type last, and preservation of existing metadata.

The input compatibility check found four acceptance differences.

| Input form | Baseline reader plus Firely | REDox reader plus Firely |
|---|---|---|
| Trailing comma | Accepts | Rejects |
| JSON comment | Accepts | Rejects |
| Single-quoted names and values | Accepts | Rejects |
| Text after the root object | Accepts | Rejects |

This is not evidence that REDox mishandles valid FHIR JSON.
It means the strict prototype does not preserve the existing reader's leniency.
Changing that policy was excluded from this experiment.
Verification therefore returns exit code `3` and `readyForIntegration=false`.
Diagnostic timing on the valid corpus does not waive this failed gate.

Both readers accepted the duplicate-property probe and rejected an invalid
literal and an unknown FHIR field. Duplicate-property resulting values and HTTP
error responses were not compared. The literal cases are screening coverage,
not the complete compatibility matrix required for integration.

## What the instrument does

`RedoxJsonReader` parses a REDox DOM and exposes Newtonsoft tokens through a
recursive iterator. Firely still interprets FHIR and creates its normal models.
The measurement includes DOM parsing, traversal, conversion, allocation, and
disposal. It does not use REDox parallel object deserialization.

The reader result applies to this adapter, not every possible REDox integration.
Recursive traversal allocates iterators. The baseline reader also lacks the
API formatter's pooled character arrays. Neither candidate calls the complete
API formatter or `FirelyImportResourceParser`.

The roundtrip adds Firely serialization. It excludes import metadata policy,
reference handling, search indexing, job orchestration, and SQL.
It must not be described as an `$import` benchmark.

The metadata instrument reproduces the parse, update, and encode operation
with a fixed version and timestamp. Both variants return a byte array.
Its baseline uses `MemoryStream.ToArray()`, whereas production writes to the
response stream. The allocation saving cannot be transferred directly to HTTP
responses. The optimized direct-copy response path was not changed or measured.

## Reproduction and evidence

The [runner instructions](../../tools/RedoxExperiment/README.md) reproduce the
instrument build, correctness checks, and fixed measurement schedule.
The [manifest](../../tools/RedoxExperiment/results/2026-10-04/manifest.json)
records source hashes, assembly hashes, and the exact run order.

| Setting | Recorded value |
|---|---|
| Server baseline source | `87c599ab28f6146a5544146cca2c3543b37e2a3e` |
| REDox source | v1.0.0, `47a9b74442e8fc3456163fd8a02db9245a111bed` |
| Firely and Newtonsoft | 5.11.4 and 13.0.4 |
| Runtime and SDK | .NET 10.0.11 and SDK 10.0.400 |
| GC | Server GC, `DOTNET_PROCESSOR_COUNT=2` |
| Host | Windows 11, AMD EPYC 7763, 16 visible logical processors, shared workloads |
| Corpus | Existing deterministic import corpus, 1,000 resources across 18 types |
| Decompressed input | 1,184,486 bytes, individual lines from 421 to 13,711 bytes |
| Warm-up | At least three seconds per process |
| Measured operations per process | 20,000 parse, 20,000 roundtrip, 1,000,000 metadata |
| Repetitions | Two baseline-control pairs and five balanced, shuffled candidate pairs per workload |
| Analysis | Paired log-ratio mean, percentile bootstrap, 20,000 resamples, seed 239717 |

The decompressed corpus SHA-256 is
`1a49c68a6c7ee3887bd204aba8a3b422342a7206268fbba2d7dce84ed1a8fd8c`.
This small-resource corpus excludes large attachment and bundle stress cases.
The processor setting limits runtime parallelism decisions and GC heap count.
It does not reserve processors, set affinity, or impose an operating-system
CPU quota.

The baseline-only pilot fixed run lengths before candidate timings were read.
All 42 scheduled processes completed. No timing sample was discarded.
The analysis script checks schedule completeness and identical run identities.
The five-pair bootstrap intervals are exploratory and do not remove host noise,
limited corpus coverage, or differences from the production call paths.

The source-built dependency and isolated instrument compiled successfully.
The instrument's literal and corpus assertions passed.
Its compatibility gate failed as described above.
The analysis self-tests and complete-run checks passed.
The full R4 Web build was canceled without a result before timing began.
It is not recorded as a successful server build.

## Remaining application experiment

The owner has not approved the proposed disposable SQL environment.
The approval prompt returned user-unavailable, not approval.
No database or server was created, reset, or deleted.
Docker was unavailable, and existing LocalDB databases were left untouched.

Application claims still require an approved isolated environment, a passing
input policy and formatter compatibility check, and measurements of actual
incremental `$import` and API requests. They also require the baseline profile,
large-resource corpus, production stream behavior, search-index equivalence,
and sustained service-memory measurements from the design.

The present evidence does not justify spending that effort on this adapter.
Keep the current production serializers. Reconsider only with a narrower
candidate that preserves behavior and shows a component benefit without the
observed CPU tradeoff.

The Laziness Protocol kept the experiment outside production code and retained
Firely semantics. Prove It Works kept component findings separate from
unmeasured application claims.
