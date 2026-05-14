# ADR 2605: Stale Job Queue Monitoring
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
FHIR background operations such as import, export, reindex, and other async requests are coordinated through SQL job queues. A queue can become stalled when jobs remain in the `Created` state but no worker is actively processing jobs for that queue. Without a queue-level signal, this condition may remain hidden until customers observe delayed operation completion or downstream operational effects.

The server already has a SQL watchdog infrastructure that runs leased background checks from `WatchdogsBackgroundService`, and it already emits application metrics through the `FhirServer` meter. The stale job monitor should reuse those mechanisms rather than introduce a separate scheduler, data store, or alerting subsystem.

## Decision
We will add a `StaleJobWatchdog` that runs as part of the existing SQL watchdog background service. On each watchdog tick it will query `SqlQueueClient.GetActiveJobsByQueueTypeAsync` for each `QueueType` except `Unknown`, using `returnParentOnly: false`, and compute an age in seconds for each queue.

The queue age calculation is per queue:

- If a queue has any `Running` job, that queue emits `0`.
- If a queue has no `Running` jobs and has one or more `Created` jobs, that queue emits the age in seconds of the oldest `Created` job.
- If a queue is empty, or has no active `Created` jobs, that queue emits `0`.
- A running job in one queue does not hide staleness in another queue.

The watchdog will publish the computed values through `StaleJobMetricsNotification`. `StaleJobMetricHandler` will keep the latest snapshot and expose it as an `ObservableGauge<double>` named `Jobs.OldestQueuedAgeSeconds` on the `FhirServer` meter, with unit `s` and a `queue_type` tag containing the `QueueType` name. Operators can alert on the exported metric value, for example when a queue age remains greater than 600 seconds.

The watchdog will use the existing watchdog defaults and lease behavior: a 60 second polling period, a 300 second lease period, and rebalance enabled. If a SQL query fails, the exception will propagate through the watchdog base class and the tick will not publish partial metric data. Queues with positive ages will also be logged with `QueueType` and `OldestJobAgeSecs` so the metric has a corresponding operational log signal.

This change does not require a SQL schema migration. It reads existing job queue state and emits metrics only.

### Decision (continued): Queue Depth Metric

The same per-tick `GetActiveJobsByQueueTypeAsync` result set is also used to compute a count of active jobs per queue. The watchdog publishes both the age map and the depth map in a single `StaleJobMetricsNotification` so the handler snapshot remains atomic across both metrics.

`StaleJobMetricHandler` exposes a second instrument — an `ObservableGauge<long>` named `Jobs.QueueDepth` on the `FhirServer` meter, with unit `{job}` — carrying two tags: `queue_type` (the `QueueType` name) and `state` (`pending` or `running`). `pending` counts jobs in the `Created` status; `running` counts jobs in the `Running` status. All other `JobStatus` values are excluded because `GetActiveJobsByQueueTypeAsync` only returns `Created` and `Running` jobs. The gauge emits two measurements per queue type per observation cycle, keeping cardinality at `queue_types × 2`.

Every non-`Unknown` `QueueType` key is always present in the snapshot, including queues with zero counts, so the gauge always reports the full set — the same contract as `Jobs.OldestQueuedAgeSeconds`.

## Status
Accepted

## Consequences
### Benefits
- Stalled async job queues become observable before customers report delayed operations.
- The signal is per queue type, so healthy activity in one queue does not mask a blocked queue elsewhere.
- The implementation reuses the existing SQL watchdog lease, retry, logging, dependency injection, and metrics patterns.
- No database schema change is required.
- Queue size (`Jobs.QueueDepth`) complements queue age (`Jobs.OldestQueuedAgeSeconds`): depth catches sudden bursts of new work before they become old, while age catches a single stuck job that keeps growing older.

### Adverse Effects
- Each watchdog tick queries every known `QueueType`, adding lightweight periodic SQL reads.
- Alert thresholds must be tuned by operators so intentionally idle queues or long-running operational patterns do not create noisy alerts.
- The metric reports detection state only; it does not automatically repair stuck queues or restart workers.

### Neutral Effects
- Age is based on application `DateTime.UtcNow` and job `CreateDate`. This is sufficient for minute-scale stale queue detection, but should not be used for precise cross-node event ordering.
- `QueueType.Unknown` is intentionally excluded because it is not an actionable job queue.
- Exported metric names may be transformed by the configured metrics exporter, but the source instruments remain `Jobs.OldestQueuedAgeSeconds` and `Jobs.QueueDepth` on the `FhirServer` meter.
- The `state` tag values for `Jobs.QueueDepth` are the stable strings `pending` and `running`. Other `JobStatus` values (`Completed`, `Failed`, `Cancelled`, `Archived`, `CancelledByUser`) are excluded because `GetActiveJobsByQueueTypeAsync` only returns `Created` and `Running` jobs.

