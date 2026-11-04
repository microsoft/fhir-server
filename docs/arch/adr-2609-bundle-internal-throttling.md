# ADR-2609: Internal Throttling of Conditional Operations and Expanded Entry Limits in Bundles

**Status**: Proposed
**Date**: 2026-09-25
**Feature**: bundle-internal-throttling

## Context

Bundle processing (`transaction`/`batch`) executes each entry as an internal request against the same FHIR service instance. Two categories of entries create disproportionate load relative to a single HTTP request:

1. **Conditional operations** (conditional create/update/delete, resolved via search-then-write). Each one issues at least one extra search query before its write. A bundle composed largely of conditional entries can multiply the effective query volume by an order of magnitude compared to the entry count, independent of the bundle's total size.
2. **Bundles larger than the normal configured entry limit ("expanded"/high-latency bundles).** Even with only simple CRUD entries, a very large parallel bundle can still saturate downstream search/storage capacity if all entries are dispatched without any pacing.

`BundleConfiguration.EntryLimit` defaults to 500 entries. `IFhirRuntimeConfiguration.IsBundleExpandedSupported` is the capability flag for bundles above that limit and is `true` only for `AzureHealthDataServicesRuntimeConfiguration`; it is `false` for `AzureApiForFhirRuntimeConfiguration`. The expanded-entry-limit capability (`BundleConfiguration.EntryLimitExpanded`) is therefore AHDS-only by design, consistent with other AHDS-exclusive capabilities gated behind `IFhirRuntimeConfiguration`.

`ThrottlingMiddleware` already exists but only throttles at the outer HTTP request boundary (concurrent-request limit with a wait queue and 429s). It has no visibility into work generated *inside* a single bundle request, so it cannot prevent a single large or conditional-heavy bundle from generating an internal burst of search/write calls that individually stay under the outer concurrency limit but collectively overload the data store.

Only `BundleProcessingLogic.Parallel` dispatches entries concurrently via `Task`-per-entry; `Sequential` bundles process one entry at a time and are inherently self-limiting. Any internal pacing mechanism, and any relaxation of the standard entry cap, is therefore only meaningful when paired with parallel bundle execution. This is an implementation constraint around the FHIR `batch` and `transaction` interactions; the expanded-bundle request signal and throttling behavior are server-specific and do not change the interaction semantics defined by the [FHIR specification](https://hl7.org/fhir/http.html#transaction).

## Options Considered

1. **Rely solely on the outer `ThrottlingMiddleware`** *(rejected: the middleware throttles per-HTTP-request concurrency; it cannot see or pace the N internal search/write calls fan-out generated inside one already-admitted bundle request)*
2. **Add a dedicated internal rate limiter/semaphore per bundle** *(rejected for now: introduces a new configurable subsystem and cross-request coordination for something the existing counter-based pacing already addresses adequately)*
3. **Bound expanded batch concurrency and pace parallel dispatch with configurable group controls** *(viable — implemented)*: expanded batches execute each HTTP-verb group in bounded chunks; expanded transactions insert a short delay at configured dispatch intervals; and non-expanded AHDS bundles pace conditional operations at the same configured interval. The group size and delay are configured by `BundleExpandedGroupSize` and `BundleExpandedGroupDelayInMilliseconds` (defaults: 50 entries and 7 ms).
4. **Raise `EntryLimit` uniformly for all bundles/backends** *(rejected: Sequential bundles process one entry at a time with no fan-out risk from raising the cap, but nothing bounds the extra load an expanded Sequential bundle would still place on request duration/timeouts; and Azure API for FHIR has no equivalent internal pacing or capacity headroom to safely accept bundles above its configured normal limit)*

## Decision

Bundle entry-count and internal throttling behavior is scoped by two independent axes: **runtime (AHDS vs. Azure API for FHIR)** and **processing logic (Parallel vs. Sequential)**.

- **Entry limit**: `BundleConfiguration.EntryLimit` is the normal limit. `BundleConfiguration.EntryLimitExpanded` is applied only when `IFhirRuntimeConfiguration.IsBundleExpandedSupported` is `true` *and* the caller opts in through the expanded-bundle request signal (`IsExpandedBundleEnabled()`). Only `AzureHealthDataServicesRuntimeConfiguration` enables this capability; Azure API for FHIR ignores the signal and retains its configured normal limit.
- **Expanded bundles require parallel processing**: an accepted expanded bundle is forced to `BundleProcessingLogic.Parallel`, regardless of the requested or default processing logic. The effective processing logic and expanded-bundle status are included in `BundleResponseInfo` for downstream metrics.
- **Expanded batch throttling**: entries are still grouped by HTTP verb, but each verb group is divided into chunks of at most `BundleExpandedGroupSize`. A chunk must complete before the next chunk for that verb is dispatched, bounding concurrent internal requests without adding a separate limiter.
- **Expanded transaction throttling**: transaction entries remain in the existing parallel transaction path, with `BundleExpandedGroupDelayInMilliseconds` pacing applied at `BundleExpandedGroupSize` dispatch intervals.
- **Conditional-operation throttling**: for non-expanded parallel bundles on AHDS, each `BundleExpandedGroupSize`-th conditional operation incurs the configured delay. This path does not run for Azure API for FHIR, expanded bundles, or sequential processing.

This keeps the capability boundary aligned with the existing `IFhirRuntimeConfiguration` pattern used for other AHDS-only features, avoids adding a new throttling subsystem, and allows the pacing values to be tuned through bundle configuration.

## Consequences

- Azure API for FHIR behavior is unchanged: it uses the normal configured entry limit and receives none of the new expanded or conditional-operation throttling.
- On AHDS, opting into the expanded limit also opts into parallel execution. Callers cannot combine expanded limits with sequential ordering.
- Expanded batches have a hard per-chunk concurrency bound, while expanded transactions use dispatch pacing because their transaction orchestration must remain intact.
- Conditional-operation-heavy, non-expanded parallel bundles on AHDS receive internal pacing, reducing pressure on search and SQL connection capacity at the cost of a small configurable latency increase.
- Expanded-bundle throttling takes precedence over conditional-operation throttling. An expanded bundle with conditional entries receives only its bundle-type-specific expanded throttling.
- The effective processing logic and expanded status are observable through bundle response metrics.
- Throttling remains local to each bundle and does not coordinate across concurrent bundles. `ThrottlingMiddleware` remains the cross-request safeguard.
