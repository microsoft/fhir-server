# ADR 2605: Stale Job Queue Monitoring
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
FHIR background operations such as import, export, reindex, and other async requests are coordinated through SQL job queues. A queue can become stalled when jobs remain in the `Created` state but no worker is actively processing jobs for that queue. Without a queue-level signal, this condition may remain hidden until customers observe delayed operation completion or downstream operational effects.

The server already has a SQL watchdog infrastructure that runs leased background checks from `WatchdogsBackgroundService`, and it already emits application metrics through the `FhirServer` meter. The stale job monitor should reuse those mechanisms rather than introduce a separate scheduler, data store, or alerting subsystem.

## Decision
We will add a `JobMonitorWatchdog` that runs as part of the existing SQL watchdog background service. On each watchdog tick it will run a single lightweight aggregate query against `dbo.JobQueue`:

```sql
SELECT QueueType, Status, MIN(CreateDate) AS OldestCreateDate, COUNT(*) AS JobCount
FROM dbo.JobQueue
WHERE Status IN (0, 1) -- Created, Running
GROUP BY QueueType, Status
```

This query is executed through `ISqlRetryService.ExecuteReaderAsync` with `isReadOnly: true`, mirroring `GeoReplicationLagWatchdog`. It returns only `QueueType`, `Status`, the oldest `CreateDate`, and a count per group — it never touches the `varchar(max)` `Definition`/`Result` payloads. This replaces the earlier approach of calling `SqlQueueClient.GetActiveJobsByQueueTypeAsync` per queue type, which resolved to `dbo.GetActiveJobs` → `dbo.GetJobs` with `@ReturnDefinition = 1` and transferred the full job payloads of every active job even though only `Status` and `CreateDate` are used.

The queue age calculation is per queue and reports the raw queue-lag signal:

- If a queue has one or more `Created` jobs, that queue emits the age in seconds of the oldest `Created` job — unconditionally, regardless of whether other jobs in the queue are `Running`. The metric reports what was measured; health judgments ("stalled") are composed downstream in logs and alert rules. This keeps a partially stalled queue visible (for example, a wedged `Running` orchestrator with starving `Created` siblings).
- If a queue is empty, or has no `Created` jobs, that queue emits `0`.
- A running job in one queue does not hide staleness in another queue.
- The computed age is clamped to a minimum of `0` (`Math.Max(0, ...)`). Because the age is `utcNow - CreateDate` where `CreateDate` is SQL-stamped and `utcNow` is the app server clock, clock skew can otherwise produce a small negative value.

The watchdog will publish the computed values through `JobMonitorMetricsNotification`. `JobMonitorMetricHandler` will keep the latest snapshot and expose it as an `ObservableGauge<double>` named `Jobs.OldestQueuedAge` on the `FhirServer` meter, with unit `s` and a `queue_type` tag containing the `QueueType` name. Operators detect a stalled queue by composing the two instruments, for example: `Jobs.OldestQueuedAge > 600` AND `Jobs.QueueDepth{state="running"} == 0` for the same `queue_type`.

The handler suppresses stale snapshots: if no notification has been handled within `SnapshotStaleCutoffSeconds` (300 seconds, 5× the publish period), both gauges emit no measurements rather than re-reporting the last snapshot. Absent data lets "no data" alerts fire; a frozen value would read as healthy exactly when the monitor itself is broken (SQL outage, lease moved to another instance, shutdown).

Metric conventions established here, applying to future instruments on the `FhirServer` meter: names follow PascalCase dot-separated `<Area>.<Measurement>` with the unit never encoded in the name (it is declared as a UCUM `unit:` so exporters do not double-suffix, e.g. Prometheus appending `_seconds`); every instrument declares a unit and a one-sentence description; tag keys are snake_case with values from a closed low-cardinality set; metrics report raw signals with judgments composed in alerts; and snapshot-backed observable gauges suppress emission when stale. Existing shipped instruments (`Search.Latency`, `Crud.Latency`, the `ExceptionType` tag, etc.) keep their names — renaming deployed instruments breaks dashboards.

The watchdog will use the existing watchdog defaults and lease behavior: a 60 second polling period, a 300 second lease period, and rebalance enabled. The tick body is wrapped so that any failure is logged via `LogError` with context that queue metrics were not refreshed this cycle, and the exception is then rethrown. The watchdog base class (`FhirTimer`) catches it to keep the loop alive on the next tick, while rethrowing preserves the all-or-nothing publish: no partial metric data is published when the query fails.

A queue is judged stalled — and logged at `LogWarning` with `QueueType` and `OldestJobAgeSecs` — when its age meets the warning threshold (`StaleQueueWarningThresholdSeconds = 600`, matching the alerting guidance above) **and** the queue has zero `Running` jobs. A busy queue with old `Created` jobs but active workers is making progress, not stalled. Queues with a positive age that do not meet both conditions are logged at `LogDebug` only, so the warning log stays quiet for healthy busy systems.

The watchdog is gated by a `CoreFeatureConfiguration.EnableJobMonitor` flag (default `true`, since this is a monitoring feature). When disabled, `WatchdogsBackgroundService` does not start the watchdog, so no periodic SQL reads or metric publications occur. The MediatR handler (`JobMonitorMetricHandler`) registration is left intact in either state — it is harmless without a publisher.

This change does not require a SQL schema migration. It reads existing job queue state and emits metrics only.

### Decision (continued): Queue Depth Metric

The same per-tick aggregate result set is also used to compute a count of active jobs per queue. The watchdog publishes both the age map and the depth map in a single `JobMonitorMetricsNotification` so the handler snapshot remains atomic across both metrics.

`JobMonitorMetricHandler` exposes a second instrument — an `ObservableGauge<long>` named `Jobs.QueueDepth` on the `FhirServer` meter, with unit `{job}` — carrying two tags: `queue_type` (the `QueueType` name) and `state` (`pending` or `running`). `pending` counts jobs in the `Created` status; `running` counts jobs in the `Running` status. All other `JobStatus` values are excluded because the aggregate query's `WHERE Status IN (0, 1)` clause only returns `Created` and `Running` jobs. The gauge emits two measurements per queue type per observation cycle, keeping cardinality at `queue_types × 2`.

Every non-`Unknown` `QueueType` key is always present in the snapshot, including queues with zero counts, so the gauge always reports the full set — the same contract as `Jobs.OldestQueuedAge`.

## Status
Accepted

## Consequences
### Benefits
- Stalled async job queues become observable before customers report delayed operations.
- The signal is per queue type, so healthy activity in one queue does not mask a blocked queue elsewhere.
- The implementation reuses the existing SQL watchdog lease, retry, logging, dependency injection, and metrics patterns.
- No database schema change is required.
- Queue size (`Jobs.QueueDepth`) complements queue age (`Jobs.OldestQueuedAge`): depth catches sudden bursts of new work before they become old, while age catches a single stuck job that keeps growing older. Stalled-queue alerting composes the two.

### Adverse Effects
- Each watchdog tick runs one lightweight aggregate SQL read over `dbo.JobQueue` (grouped by `QueueType` and `Status`, restricted to active statuses, returning only counts and the oldest `CreateDate`). It does not read job payloads.
- Alert thresholds must be tuned by operators so intentionally idle queues or long-running operational patterns do not create noisy alerts.
- The feature can be turned off via `CoreFeatureConfiguration.EnableJobMonitor`; when disabled the metrics are not produced.
- The metric reports detection state only; it does not automatically repair stuck queues or restart workers.

### Neutral Effects
- Age is based on application `DateTime.UtcNow` and job `CreateDate`. This is sufficient for minute-scale stale queue detection, but should not be used for precise cross-node event ordering.
- `QueueType.Unknown` is intentionally excluded because it is not an actionable job queue.
- Exported metric names may be transformed by the configured metrics exporter, but the source instruments remain `Jobs.OldestQueuedAge` and `Jobs.QueueDepth` on the `FhirServer` meter.
- The `state` tag values for `Jobs.QueueDepth` are the stable strings `pending` and `running`. Other `JobStatus` values (`Completed`, `Failed`, `Cancelled`, `Archived`, `CancelledByUser`) are excluded by the aggregate query's `WHERE Status IN (0, 1)` clause.

