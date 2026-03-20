# ADR 2603: Reindex Cache Race Condition — MediatR Notification-Based Coordination

Labels: [Core](https://github.com/microsoft/fhir-server/labels/Area-Core) | [SearchParameter](https://github.com/microsoft/fhir-server/labels/Area-SearchParameter) | [Reindex](https://github.com/microsoft/fhir-server/labels/Area-Reindex)

## Context

Two intermittent E2E test failures in the reindex pipeline pointed to cache race conditions in multi-instance deployments.

### Issue 1: Search parameter stays NotSupported after reindex

The orchestrator job calls `RefreshSearchParameterCache` before creating processing jobs. Previously this used a passive `Task.Delay(multiplier * interval)` to wait for the background cache refresh service to run. However, a delay does **not guarantee** the service actually completed a refresh. If the SearchParameter was created on a different host, the orchestrator's local cache may never have seen it, causing the parameter to be silently skipped and left as `NotSupported` even though the reindex completed.

**Evidence from EventLog (E2E run):**

| Time | Event | Detail |
|------|-------|--------|
| 12:24:16 | Last background cache refresh on host `c7788f45efbb` | Rows=0, watermark at 12:24:06 |
| 12:24:27 | `custom-diff-type-75a28a96` created on host `c7788f45efbb` | Added to local cache |
| 12:24:29 | `custom-diff-status-75a28a96` created on host `0ae6d064898e` | Added to *that* host's cache only |
| 12:24:29 | Reindex Job 15 starts on `c7788f45efbb` | Passive `Task.Delay` wait begins (~3-6 s) |
| 12:24:35 | Orchestrator checks definition manager cache | `custom-diff-status-75a28a96` NOT found (EventId 2256: status=null) |
| 12:24:53 | Next background refresh on `c7788f45efbb` | **37-second gap** — too late |

The orchestrator's passive delay expired before the background service ran, so the parameter created on the other host was never picked up.

### Issue 2: Reindex job reports Failed despite 100% resource success

Processing jobs read the search parameter hash from their local in-memory cache and write it to each reindexed resource. If the cache is stale (hash mismatch with the orchestrator's hash), the job logs a warning but previously continued with the wrong hash. Post-completion verification then finds the mismatch and marks the job as failed.

**Evidence from EventLog (E2E run):**

| Time | EventId | Detail |
|------|---------|--------|
| 12:27:49 | — | Orchestrator records `SearchParamLastUpdated=12:27:49.310` |
| 12:28:00 | — | Orchestrator Job 22 starts, creates processing job with hash `BEAA3D...` |
| 12:28:07 | 2492 | Processing Job 23 on host `1c7b3415077c`: `SearchParameterHash: Requested=BEAA3D... != Current=56F8EE...` — host has stale cache |
| 12:28:07 | 2493 | Same job: `SearchParamLastUpdated: Requested=12:27:49.310 > Current=12:27:46.080` — cache is 3 seconds behind |
| 12:28:07 | — | Processing job proceeds anyway using its local hash `56F8EE` → writes it to the resource |
| 12:28:08 | — | Orchestrator verification queries with current hash → finds 1 resource not matching → reports "failed to reindex" |

The processing job detected both the hash mismatch and the timestamp lag, logged them as errors, but proceeded with stale data. The orchestrator's subsequent verification used a different hash, finding the mismatch and reporting failure.

### Root cause

Both issues stem from the same root cause: **the reindex pipeline had no deterministic signal that the background cache refresh had actually completed**. The passive `Task.Delay` was a time-based guess that could not account for variable refresh intervals, host-to-host propagation delays, or cases where the background service had not yet run.

## Decision

We introduce a `SearchParameterCacheRefreshedNotification` MediatR notification that the `SearchParameterCacheRefreshBackgroundService` publishes after each successful `GetAndApplySearchParameterUpdates` call. `SearchParameterOperations` handles this notification and exposes a `WaitForRefreshCyclesAsync(int cycleCount, CancellationToken)` method for jobs to deterministically wait for N completed refresh cycles.

### Notification flow

```
SearchParameterCacheRefreshBackgroundService
  └─ OnRefreshTimer()
       ├─ GetAndApplySearchParameterUpdates()
       └─ _mediator.Publish(SearchParameterCacheRefreshedNotification)
              │
              ▼
SearchParameterOperations : INotificationHandler<SearchParameterCacheRefreshedNotification>
  └─ Handle()
       └─ swap-and-complete TaskCompletionSource (_refreshSignal)
              │
              ▼
ReindexOrchestratorJob / ReindexProcessingJob
  └─ await _searchParameterOperations.WaitForRefreshCyclesAsync(N, ct)
       └─ loops N times, each iteration awaiting _refreshSignal.Task
```

### Key changes

- **`SearchParameterCacheRefreshedNotification`** — new signal-only `INotification`.
- **`SearchParameterCacheRefreshBackgroundService`** — accepts `IMediator`; publishes notification after successful refresh.
- **`SearchParameterOperations`** — implements `INotificationHandler<SearchParameterCacheRefreshedNotification>`; uses a volatile `TaskCompletionSource<bool>` for efficient async signaling via swap-and-complete pattern.
- **`ISearchParameterOperations`** — new method `WaitForRefreshCyclesAsync(int, CancellationToken)`.
- **`ReindexOrchestratorJob`** — `RefreshSearchParameterCache` replaced `Task.Delay` with `WaitForRefreshCyclesAsync(CacheRefreshWaitMultiplier)`.
- **`ReindexProcessingJob`** — `CheckDiscrepancies` on hash mismatch waits 3 refresh cycles, re-checks, and throws `ReindexJobException` if still mismatched (fail-fast instead of silently proceeding with a wrong hash).

### Design constraints honored

- Cache refresh remains the **sole responsibility** of `SearchParameterCacheRefreshBackgroundService`. Jobs never call refresh directly.
- Follows existing MediatR notification patterns in the codebase (cf. `SearchParametersUpdatedNotification` → `SearchParameterDefinitionManager`).
- `SearchParameterOperations` is already injected into both job types, making it the natural handler.

## Status

Pending

## Consequences

- **Positive:** Eliminates both race conditions by replacing time-based guessing with event-driven coordination. Processing jobs fail fast on persistent hash mismatch instead of silently writing incorrect data.
- **Neutral:** Adds one MediatR notification per background refresh cycle — negligible overhead given the 20-second (production) / 1-second (E2E) timer interval.
- **Negative:** Jobs now block until the background service completes N cycles. If the background service is unhealthy or stopped, jobs will block until the cancellation token fires. This is the desired fail-safe behavior — a stale cache should not silently produce incorrect reindex results.
