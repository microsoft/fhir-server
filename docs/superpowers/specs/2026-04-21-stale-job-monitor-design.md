# Stale Job Monitor Design

**Date:** 2026-04-21
**Branch:** feature/stale-job-monitor

## Problem

When no jobs are running (zero `JobStatus.Running` entries) but jobs remain queued (`JobStatus.Created`), the queue may be stalled — worker failure, deadlock, or misconfiguration. Without a signal, this goes undetected until someone notices downstream effects.

## Goal

Emit a Prometheus gauge per queue type showing the age in seconds of the oldest queued job when no jobs are running. Alert when `fhir_oldest_queued_job_age_seconds > 600`.

## Architecture

```
StaleJobWatchdog (60s poll, singleton lease)
  → IQueueClient.GetActiveJobsByQueueTypeAsync per QueueType
  → IMediator.Publish(StaleJobMetricsNotification)
    → StaleJobMetricHandler
      → ObservableGauge<double> fhir_oldest_queued_job_age_seconds{queue_type=...}
```

## Logic

On each watchdog tick:

1. For each value in `QueueType` enum excluding `QueueType.Unknown`, call `GetActiveJobsByQueueTypeAsync(queueType, returnParentOnly: false, ct)`.
2. If any job across all queue types has `Status == JobStatus.Running`, emit 0 for all queue types and return — queue is healthy.
3. Otherwise, per queue type: find the minimum `CreateDate` among `JobStatus.Created` jobs. Age = `(DateTime.UtcNow - minCreateDate).TotalSeconds`. Emit 0 if that queue has no created jobs.
4. Log a warning for each queue type where age > 0: `"Stale job queue detected. QueueType={QueueType} OldestJobAgeSecs={Age}"`.
5. Publish `StaleJobMetricsNotification(IReadOnlyDictionary<byte, double> queueTypeAges)`.

## Metric

**Name:** `fhir_oldest_queued_job_age_seconds`  
**Type:** ObservableGauge\<double\>  
**Label:** `queue_type` (string name of the `QueueType` enum value)  
**Semantics:** Age in seconds of the oldest `Created` job when no jobs are `Running`. Zero when jobs are running or queue is empty.

Example:
```
fhir_oldest_queued_job_age_seconds{queue_type="Export"}    754
fhir_oldest_queued_job_age_seconds{queue_type="Import"}      0
fhir_oldest_queued_job_age_seconds{queue_type="BulkDelete"}  0
```

Recommended alert rule: `fhir_oldest_queued_job_age_seconds > 600`

## Files

| File | Change |
|------|--------|
| `src/Microsoft.Health.Fhir.Core/Features/Operations/StaleJob/Messages/StaleJobMetricsNotification.cs` | New — notification carrying `IReadOnlyDictionary<byte, double>` |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/StaleJobWatchdog.cs` | New — `Watchdog<StaleJobWatchdog>`, implements `RunWorkAsync` |
| `src/Microsoft.Health.Fhir.Core/Logging/Metrics/StaleJobMetricHandler.cs` | New — `BaseMeterMetricHandler`, `INotificationHandler<StaleJobMetricsNotification>`, holds `ConcurrentDictionary<byte, double>` for gauge callback |
| `src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs` | Modified — add watchdog singleton + `RemoveServiceTypeExact`/re-register handler as singleton |

## DI Registration

```csharp
services.Add<StaleJobWatchdog>().Singleton().AsSelf();

services.RemoveServiceTypeExact<StaleJobMetricHandler, INotificationHandler<StaleJobMetricsNotification>>()
    .Add<StaleJobMetricHandler>()
    .Singleton()
    .AsSelf()
    .AsService<INotificationHandler<StaleJobMetricsNotification>>();
```

`StaleJobWatchdog` is automatically picked up by the existing `WatchdogsBackgroundService` — no changes needed there.

## Watchdog Configuration

| Parameter | Default | Notes |
|-----------|---------|-------|
| `PeriodSec` | 60 | Poll interval |
| `LeasePeriodSec` | 300 | Lease duration — one instance runs at a time |
| `AllowRebalance` | true | Follows existing watchdog convention |

## Error Handling

- If `GetActiveJobsByQueueTypeAsync` throws, let the exception propagate to the watchdog base class — it handles logging and retry via `ISqlRetryService`.
- No partial results: if any queue type query fails, skip the publish for that tick.
