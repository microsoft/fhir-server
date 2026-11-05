# ADR 2605: Stale Job Queue Monitoring
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
FHIR background operations such as import, export, reindex, and other async requests are coordinated through SQL job queues. A queue can become stalled when jobs remain in the `Created` state but no worker is actively processing jobs for that queue. Without a queue-level signal, this condition may remain hidden until customers observe delayed operation completion or downstream operational effects.

The server already has a SQL watchdog infrastructure that runs leased background checks from `WatchdogsBackgroundService`, and it already emits application metrics through the `FhirServer` meter. The stale job monitor should reuse those mechanisms rather than introduce a separate scheduler, data store, or alerting subsystem.

## Desired Data Output
Every 60 seconds, report Created and Running job counts and oldest Created-job age per queue type from `JobQueue`. Warn when that age is >= 600 seconds with no Running jobs.

## Constraints
Use existing indexes on the `JobQueue` table.

## Options Considered
1. **Status-only SQL aggregate over `JobQueue`** - Returns only counts and oldest dates, but lacks the leading `PartitionId` predicate needed for selective access through the existing index. Rejected because a small result set does not prevent scans of retained job history and resulting query timeouts.
2. **New partition-aware aggregate stored procedure** - Supplies queue type and each of the 16 partition IDs, then aggregates in SQL. Viable with existing indexes and a smaller result set, but introduces a separate retrieval implementation and stored-procedure contract.
3. **Existing active-job retrieval with client aggregation** - Reuses `GetActiveJobsByQueueTypeAsync` and computes counts and oldest dates in the watchdog. Chosen because the CPU assessment below predicts a small client cost at the expected scale, making a separate aggregate stored procedure unnecessary while preserving existing indexed access.

### Client CPU Estimate
Reading and materializing 10K jobs from `GetActiveJobsByQueueTypeAsync` without `Definition` and `Result` is estimated to use 20-100 ms of client CPU. With 60 second period, this averages 0.03-0.17% of one CPU core (`20-100 / 60,000 * 100`). This estimate excludes SQL execution, network wait time, and subsequent aggregation.

## Decision
We will add a `JobMonitorWatchdog` that runs as part of the existing SQL watchdog background service. On each watchdog tick it calls `GetActiveJobsByQueueTypeAsync` for every monitored queue type, then computes counts and minimum `CreateDate` values by status in the watchdog.

This reuses `dbo.GetActiveJobs` → `dbo.GetJobs` stored procedures. `GetActiveJobs` has `QueueType` input and iterates the 16 `PartitionId` values, allowing the existing partition-first index to locate Created and Running jobs without scanning the JobQueue table, which could lead to query timeouts for large customers.

The queue age calculation is per queue type and reports the raw queue-lag signal:

- If a queue has one or more `Created` jobs, that queue emits the age in seconds of the oldest `Created` job — unconditionally, regardless of whether other jobs in the queue are `Running`. The metric reports what was measured; health judgments ("stalled") are composed downstream in logs and alert rules. This keeps a partially stalled queue visible (for example, a wedged `Running` orchestrator with starving `Created` siblings).
- If a queue is empty, or has no `Created` jobs, that queue emits `0`.
- A running job in one queue does not hide staleness in another queue.
- The computed age is clamped to a minimum of `0` (`Math.Max(0, ...)`). Because the age is `utcNow - CreateDate` where `CreateDate` is SQL-stamped and `utcNow` is the app server clock, clock skew can otherwise produce a small negative value.

The watchdog will publish the computed values through `JobMonitorMetricsNotification`. `JobMonitorMetricHandler` will keep the latest snapshot and expose it as an `ObservableGauge<double>` named `Jobs.OldestQueuedAge` on the `FhirServer` meter, with unit `s` and a `queue_type` tag containing the `QueueType` name. Operators detect a stalled queue by composing the two instruments, for example: `Jobs.OldestQueuedAge >= 600` AND `Jobs.QueueDepth{state="running"} == 0` for the same `queue_type`.

The handler suppresses stale snapshots: if no notification has been handled within `SnapshotStaleCutoffSeconds` (300 seconds, 5× the publish period), both gauges emit no measurements rather than re-reporting the last snapshot. Absent data lets "no data" alerts fire; a frozen value would read as healthy exactly when the monitor itself is broken (SQL outage, lease moved to another instance, shutdown).

Metric conventions established here, applying to future instruments on the `FhirServer` meter: names follow PascalCase dot-separated `<Area>.<Measurement>` with the unit never encoded in the name (it is declared as a UCUM `unit:` so exporters do not double-suffix, e.g. Prometheus appending `_seconds`); every instrument declares a unit and a one-sentence description; tag keys are snake_case with values from a closed low-cardinality set; metrics report raw signals with judgments composed in alerts; and snapshot-backed observable gauges suppress emission when stale. Existing shipped instruments (`Search.Latency`, `Crud.Latency`, the `ExceptionType` tag, etc.) keep their names — renaming deployed instruments breaks dashboards.

The watchdog will use the existing watchdog defaults and lease behavior: a 60 second polling period, a 300 second lease period, and rebalance enabled. The tick body is wrapped so that any failure is logged via `LogError` with context that queue metrics were not refreshed this cycle, and the exception is then rethrown. The watchdog base class (`FhirTimer`) catches it to keep the loop alive on the next tick, while rethrowing preserves the all-or-nothing publish: no partial metric data is published when the query fails.

A queue is judged stalled — and logged at `LogWarning` with `QueueType` and `OldestJobAgeSecs` — when its age meets the warning threshold (`StaleQueueWarningThresholdSeconds = 600`, matching the alerting guidance above) **and** the queue has zero `Running` jobs. A busy queue with old `Created` jobs but active workers is making progress, not stalled. Queues with a positive age that do not meet both conditions are logged at `LogDebug` only, so the warning log stays quiet for healthy busy systems.

The watchdog is gated by a `CoreFeatureConfiguration.EnableJobMonitor` flag (default `true`, since this is a monitoring feature). When disabled, `WatchdogsBackgroundService` does not start the watchdog, so no periodic SQL reads or metric publications occur. The MediatR handler (`JobMonitorMetricHandler`) registration is left intact in either state — it is harmless without a publisher.

Schema version 122 adds an optional `@ReturnDefinition` parameter to `dbo.GetActiveJobs`, forwarded to `dbo.GetJobs`. Its default of `1` preserves existing callers; `0` returns the same job metadata with `Definition` set to `NULL`. Monitoring reads existing job queue state and emits metrics only.

### Decision (continued): Queue Depth Metric

The same per-tick aggregate result set is also used to compute a count of active jobs per queue. The watchdog publishes both the age map and the depth map in a single `JobMonitorMetricsNotification` so the handler snapshot remains atomic across both metrics.

`JobMonitorMetricHandler` exposes a second instrument — an `ObservableGauge<long>` named `Jobs.QueueDepth` on the `FhirServer` meter, with unit `{job}` — carrying two tags: `queue_type` (the `QueueType` name) and `state` (`pending` or `running`). `pending` counts jobs in the `Created` status; `running` counts jobs in the `Running` status. All other `JobStatus` values are excluded from the watchdog's aggregates. The gauge emits two measurements per queue type per observation cycle, keeping cardinality at `queue_types × 2`.

Every non-`Unknown` `QueueType` key is always present in the snapshot, including queues with zero counts, so the gauge always reports the full set — the same contract as `Jobs.OldestQueuedAge`.

## Status
Accepted

## Consequences
### Benefits
- Stalled async job queues become observable before customers report delayed operations.
- The signal is per queue type, so healthy activity in one queue does not mask a blocked queue elsewhere.
- The implementation reuses the existing SQL watchdog lease, retry, logging, dependency injection, and metrics patterns.
- No table or index change is required; the optional stored-procedure parameter preserves existing callers.
- Queue depth and oldest Created-job age describe backlog size and age. Neither alone identifies a worker failure or database capacity constraint; stalled-queue alerting combines age with the absence of Running jobs.

### Adverse Effects
- Each 60-second watchdog tick retrieves active jobs for each monitored queue type. Transfer and materialization costs grow with the active backlog; the existing API returns job definitions as well as the metadata used for monitoring.
- Alert thresholds must be tuned by operators so intentionally idle queues or long-running operational patterns do not create noisy alerts.
- The feature can be turned off via `CoreFeatureConfiguration.EnableJobMonitor`; when disabled the metrics are not produced.
- The metric reports detection state only; it does not automatically repair stuck queues or restart workers.

### Neutral Effects
- Age is based on the application clock (`ClockResolver.TimeProvider`, the same time source the metric handler uses for staleness) and job `CreateDate`. This is sufficient for minute-scale stale queue detection, but should not be used for precise cross-node event ordering.
- `QueueType.Unknown` is intentionally excluded because it is not an actionable job queue.
- Exported metric names may be transformed by the configured metrics exporter, but the source instruments remain `Jobs.OldestQueuedAge` and `Jobs.QueueDepth` on the `FhirServer` meter.
- The `state` tag values for `Jobs.QueueDepth` are the stable strings `pending` and `running`. Other `JobStatus` values (`Completed`, `Failed`, `Cancelled`, `Archived`, `CancelledByUser`) are excluded from the watchdog's aggregates.

