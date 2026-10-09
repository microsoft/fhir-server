# ADR-2609: Internal Throttling of Conditional Operations and Extended Entry Limits in Bundles

**Status**: Proposed
**Date**: 2026-09-25
**Feature**: bundle-internal-throttling

## Context

Bundle processing (`transaction`/`batch`) executes each entry as an internal request against the same FHIR service instance. Two categories of entries create disproportionate load relative to a single HTTP request:

1. **Conditional operations** (conditional create/update/delete, resolved via search-then-write). Each one issues at least one extra search query before its write. A bundle composed largely of conditional entries can multiply the effective query volume by an order of magnitude compared to the entry count, independent of the bundle's total size.
2. **Bundles larger than the standard 500-entry limit ("extended"/high-latency bundles).** Even with only simple CRUD entries, a very large parallel bundle can still saturate downstream search/storage capacity if all entries are dispatched without any pacing.

`BundleConfiguration.EntryLimit` caps standard bundles at 500 entries. `IFhirRuntimeConfiguration.IsBundleExtendedSupported` already exists as a capability flag, and is `true` only for `AzureHealthDataServicesRuntimeConfiguration` and `false` for `AzureApiForFhirRuntimeConfiguration` — the extended-entry-limit capability (`BundleConfiguration.EntryLimitExpanded`) is therefore AHDS-only by design, consistent with other AHDS-exclusive capabilities gated behind `IFhirRuntimeConfiguration`.

`ThrottlingMiddleware` already exists but only throttles at the outer HTTP request boundary (concurrent-request limit with a wait queue and 429s). It has no visibility into work generated *inside* a single bundle request, so it cannot prevent a single large or conditional-heavy bundle from generating an internal burst of search/write calls that individually stay under the outer concurrency limit but collectively overload the data store.

Only `BundleProcessingLogic.Parallel` dispatches entries concurrently via `Task`-per-entry; `Sequential` bundles process one entry at a time and are inherently self-limiting. Any internal pacing mechanism, and any relaxation of the 500-entry cap, is therefore only meaningful — and only needs to be applied — to parallel bundle execution.

## Options Considered

1. **Rely solely on the outer `ThrottlingMiddleware`** *(rejected: the middleware throttles per-HTTP-request concurrency; it cannot see or pace the N internal search/write calls fan-out generated inside one already-admitted bundle request)*
2. **Add a dedicated internal rate limiter/semaphore per bundle** *(rejected for now: introduces a new configurable subsystem and cross-request coordination for something the existing counter-based pacing already addresses adequately)*
3. **In-loop counter-based pacing inside `BundleHandlerParallelOperations`, gated by processing logic and runtime capability** *(viable — implemented)*: while dispatching entries in the parallel-processing loop, track a running count of conditional operations and inject a short `Task.Delay` every N (25) dispatches; separately, for extended (>500-entry) bundles, pace dispatch every N entries once the bundle exceeds 100 entries. Both mechanisms only run inside the `Parallel` code path.
4. **Raise `EntryLimit` uniformly for all bundles/backends** *(rejected: Sequential bundles process one entry at a time with no fan-out risk from raising the cap, but nothing bounds the extra load an extended Sequential bundle would still place on request duration/timeouts; and Azure API for FHIR has no equivalent internal pacing or capacity headroom to safely accept bundles above 500 entries)*

## Decision

Bundle entry-count and internal throttling behavior is scoped by two independent axes: **runtime (AHDS vs. Azure API for FHIR)** and **processing logic (Parallel vs. Sequential)**.

- **Entry limit**: `BundleConfiguration.EntryLimit` (500) is the limit for all bundles by default. `BundleConfiguration.EntryLimitExpanded` is only applied when `IFhirRuntimeConfiguration.IsBundleExtendedSupported` is `true` *and* the caller opts in via the expanded-bundle request signal (`IsExpandedBundleEnabled()`). `IsBundleExtendedSupported` is `true` only on `AzureHealthDataServicesRuntimeConfiguration`; `AzureApiForFhirRuntimeConfiguration` always returns `false`, so bundles on Azure API for FHIR remain hard-capped at 500 entries regardless of any request header.
- **Extended bundles require parallel processing**: the extended-entry-count code path is only exercised when the bundle is processed with `BundleProcessingLogic.Parallel`. Sequential bundles never receive the expanded limit or the associated pacing, and continue to be capped at 500 entries — Sequential processing has no fan-out to pace, so extending its limit would only extend request duration/timeout exposure without a corresponding throttling mechanism.
- **Internal throttling** is implemented as counter-based pacing inside the parallel dispatch loop (`BundleHandlerParallelOperations`), not as a separate limiter service, keeping the mechanism local to the one place entries fan out:
  - For extended bundles (`IsBundleExtendedSupported && _isBundleExtendedOperation`) with 100+ entries, dispatch is paused briefly every 25 entries.
  - For all other parallel bundles, regardless of size, every 25th *conditional* operation triggers the same brief pause, since conditional entries are the primary source of extra search load per entry.
  - Both mechanisms are mutually exclusive per bundle (extended-bundle pacing takes precedence when applicable) and both are no-ops for Sequential bundles.

This keeps the capability boundary aligned with the existing `IFhirRuntimeConfiguration` pattern already used for other AHDS-only features, avoids adding a new throttling subsystem, and confines the added complexity to the one execution path (parallel) where uncontrolled fan-out is possible.

## Consequences

- Azure API for FHIR behavior is unchanged: 500-entry cap, no internal pacing, no risk of the new pacing logic affecting existing customers on that backend.
- On AHDS, customers can opt into bundles larger than 500 entries only when explicitly requesting expanded bundles *and* using parallel processing; Sequential bundles are unaffected and remain simpler to reason about (strict per-entry ordering, no throttling needed).
- Conditional-operation-heavy bundles get consistent internal pacing on AHDS even below the 500-entry threshold, reducing the risk of a single bundle overwhelming search capacity, at the cost of added latency for those bundles (bounded, small per-group delay).
- The two pacing conditions (extended-size vs. conditional-count) are currently mutually exclusive per bundle; a bundle that is both extended-size *and* conditional-heavy only receives the extended-size pacing. If future data shows this under-throttles that combination, the two counters will need to be combined or the thresholds tuned rather than left as an implicit gap.
- Because pacing lives inline in the dispatch loop rather than a shared limiter, it only bounds the rate of *dispatch* within one bundle; it does not coordinate across concurrently executing bundles on the same instance. The outer `ThrottlingMiddleware` remains the only cross-request safeguard, so extreme multi-bundle concurrency is still bounded there, not here.
