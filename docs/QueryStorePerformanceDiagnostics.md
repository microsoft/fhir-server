# Query Store Performance Diagnostics - Baseline Specification

## Status

Agreed baseline for implementation. This document defines an opt-in background worker that emits Azure SQL Query Store diagnostics from inside the FHIR server, the enablement model, the emitted log records, the disclosure boundary, and the repository ownership split.

This specification **supersedes an earlier pull-based design** in which three stored procedures were called by an external caller. See [Rejected alternative](#rejected-alternative-caller-invoked-stored-procedures).

## Problem

FHIR Azure SQL performance investigations currently require privileged, manual access to Query Store plans, runtime metrics, wait statistics, and statistics metadata. Support engineers need a bounded way to:

- identify expensive or regressed query plans;
- obtain an SSMS-viewable Showplan for a slow plan;
- compare runtime and wait metrics; and
- inspect statistics freshness, sampling, and cardinality metadata.

Azure SQL diagnostic settings can already export `QueryStoreRuntimeStatistics`, `QueryStoreWaitStatistics`, and `AutomaticTuning` to Log Analytics. What that stream does **not** carry is the Query Store **query text** and the **Showplan XML**, which are the two artifacts an investigation actually needs in order to reason about a regression. This feature closes that gap.

Related internal guidance:

- `Health.wiki/Home/Olympus-Team/DRI/TSGs/SQL-Latency-Issues.md`
- `Health.wiki/Home/Olympus-Team/Knowledge-Base/Datastore/SQL-DB/SQL-Query-Store---help-with-analyzing-queries.md`
- `Health.wiki/Home/Olympus-Team/DRI/TSGs/SQL-Latency-Issues/SQL-Statistics-Overview.md`
- `Health.wiki/Home/Olympus-Team/Development/SQL-Performance-Automation.md`

## Design

A **watchdog** — the repository's existing leased background-worker pattern — periodically reads Query Store and statistics metadata and writes the results out as **structured log records**. It runs inside the FHIR server process on the server's **existing** SQL identity.

```text
FHIR server instance (lease holder)
  └── QueryStoreDiagnosticsWatchdog        every PeriodSec, default 3600s
        ├── sys.database_query_store_options      state gate, always primary
        ├── sys.query_store_*                     slow plans + query text
        ├── sys.query_store_wait_stats            best effort
        ├── sys.stats / sys.dm_db_stats_properties
        └── QueryPlanSanitizer (C#)               strips parameter values
              │
              └── ILogger<QueryStoreDiagnosticsWatchdog>   structured log records
                    └── the host's existing log pipeline (PaaS -> Geneva / Log Analytics)
```

The critical property is that **nothing new connects inbound to the database**. The work is done by the service that already holds a connection, so the feature introduces no new authentication path, no new database principal, and no new grant.

### Why a watchdog

`WatchdogsBackgroundService` and `WatchdogLease<T>` already provide what this feature needs, and each of these would otherwise have to be reinvented:

- **Single-runner election.** `WatchdogLease<T>` ensures exactly one instance in a multi-instance deployment performs the work, so an eight-instance service does not issue eight concurrent Query Store scans and emit eight copies of every diagnostics line. The lease is invisible runtime coordination rather than configuration: it holds nothing an operator sets, and it lives in `dbo.WatchdogLeases` behind `dbo.AcquireWatchdogLease`, shared with every other watchdog.
- **A managed background timer.** `FhirTimer` supplies the randomized start-up stagger that keeps replicas from collecting on the same second, and catches whatever a tick throws so that a failed collection costs one tick rather than the process.
- **A precedent for emitting SQL telemetry.** `GeoReplicationLagWatchdog` reads a SQL view on a timer and emits what it finds. This feature has the same shape, but emits **logs rather than metric notifications**; see [Why logs rather than metrics](#why-logs-rather-than-metrics). A single lag figure is metric-shaped, and query text and plan XML are not.

**It derives from `Watchdog<T>` like every other watchdog, and keeps configuration authoritative through the one hook the base class provides.** The base class inserts `{Name}.PeriodSec` and `{Name}.LeasePeriodSec` into `dbo.Parameters` on every start and then reads both back **over** the configured values, from a private, non-virtual initialization step. That read-back is not harmless: `dbo.Parameters` is declared `PRIMARY KEY CLUSTERED (Id) WITH (IGNORE_DUP_KEY = ON)`, so on a database that already holds those rows the seeding `INSERT` is a silent no-op — it neither inserts nor errors — and the stored value therefore wins over the environment variable. Verified directly against SQL Server: inserting `3600` and then `60` for the same `Id` reports *"Duplicate key was ignored. (0 rows affected)"* and leaves `3600` in place.

The base class does expose one overridable step, `InitAdditionalParamsAsync`, which runs after that read-back and before the timer is constructed. This watchdog overrides it to `UPDATE` both rows to the configured values and re-assign the two properties, so configuration wins on every database and the rows stay an accurate mirror of the deployment's settings rather than a stale copy that silently contradicts them. An `UPDATE` is used rather than an `INSERT` precisely because `IGNORE_DUP_KEY` would make a re-`INSERT` a no-op. Deriving from the base class means the lease-holder gate, the capped randomized stagger, and the per-tick timing line come from shared code rather than being reproduced here, and no shared file is modified for this feature.

### Enablement

The feature is **off by default** and is gated by one switch, in configuration.

| Gate | Location | Purpose |
| --- | --- | --- |
| `FhirServer:Watchdog:QueryStoreDiagnostics:Enabled` | Host configuration | When false the watchdog is never started by `WatchdogsBackgroundService`, and no Query Store read occurs. |

**Every setting for this feature is set in configuration, and configuration always wins.** There is no row to seed or arm before the feature will run: no `IsEnabled` row, and nothing an operator has to write by hand. Turning the feature on, tuning it, and turning it off are configuration changes plus a restart, with no `UPDATE` against a live database required of anyone.

The base class does keep `{Name}.PeriodSec` and `{Name}.LeasePeriodSec` in `dbo.Parameters`, and this watchdog reconciles both rows to the configured values during initialization, so a database can never hold a value that disagrees with the deployment's configuration. The rows are a readable mirror of what the service is running with, not an input to it — editing one changes nothing, because the next start overwrites it.

The watchdog also re-reads `Enabled` at the start of every tick and returns without collecting when it is false. That check cannot be reached with the value false as the host is wired today: `WatchdogsBackgroundService` gates startup on the same configuration snapshot, and `IOptions<T>` does not reload in place. It is kept because the watchdog is registered `AsSelf` and this is the only place the opt-out is enforced at the unit of work, so a future call site that executes a collection directly cannot bypass it. The cost is one boolean read per period.

This feature causes three database rows to exist. Two are its `dbo.Parameters` rows, described above, which mirror configuration rather than drive it. The third is its **lease**, in `dbo.WatchdogLeases` through `dbo.AcquireWatchdogLease`. The lease is what stops every replica collecting and emitting the same diagnostics every period; it holds no configuration and is the same mechanism every other watchdog uses. That shared stored procedure does consult `dbo.Parameters` for the fleet-wide watchdog lease-holder include and exclude patterns, which is noted under [Configuration](#configuration): it is shared framework behaviour that this feature neither sets nor reads for itself.

### Configuration

`WatchdogConfiguration.QueryStoreDiagnostics`, bound from `FhirServer:Watchdog:QueryStoreDiagnostics`. Note that `Watchdog` is a sibling of `Operations` under `FhirServer`, not nested inside it:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | The gate described above. |
| `PeriodSec` | `3600` | Interval between collections, and — clamped to `[60, 86400]` seconds — the Query Store lookback window each collection covers. A non-positive or non-finite value is rejected with a warning and the `3600` default is used, because `PeriodicTimer` would otherwise throw and fault every watchdog in the process. |
| `LeasePeriodSec` | `600` | How long the single-runner lease is held before it must be renewed. Long enough that a collection cannot outlive it, short enough that a dead replica's lease is picked up promptly. A non-positive or non-finite value is rejected with a warning and the `600` default is used. Most deployments should leave this alone; it is on the configuration surface because the base class stores it in `dbo.Parameters`, and every stored value must be settable from configuration. |
| `SlowQueryCount` | `10` | Number of slow plans to report per tick. |
| `MinDurationMilliseconds` | `1000` | Minimum weighted average duration for a plan to be reported. A negative value is treated as `0` — which makes every query qualify — and warns. |
| `IncludeQueryPlans` | `true` | Whether sanitized Showplan XML is emitted. |
| `IncludeStatisticsHealth` | `true` | Whether statistics metadata is emitted. |
| `StatisticsHealthCount` | `20` | Worst-ranked statistics rows **collected and reported** per tick; see [Why the count is capped](#why-the-count-is-capped). |
| `StatisticsHealthBatchSize` | `20` | How many of those rows are packed into each log line. This is **not** a second cap on what is collected: rows beyond the batch size are emitted on further lines, paginated. A non-positive value is reported with a warning and the `20` default is used, because a batch size cannot pack a row. Values above `64` are clamped, also with a warning; see [Batch size is capped](#batch-size-is-capped). |
| `RunStartDate` | `null` (unset) | Inclusive start of the optional run window. Ticks before it collect nothing. `null` means no lower bound. |
| `RunEndDate` | `null` (unset) | **Exclusive** end of the optional run window. Ticks at or after it collect nothing. `null` means no upper bound. |

The lookback window is `PeriodSec` clamped to `[60, 86400]` seconds, so the collection window tracks the collection interval and a misconfigured value cannot request an unbounded scan.

The watchdog warns on every tick where the clamp changed the value, naming the effective window and what the clamp costs, and pointing at `FhirServer:Watchdog:QueryStoreDiagnostics:PeriodSec` as the thing to change. The tick interval itself is **not** clamped, so whenever the clamp bites the interval and the window it covers are permanently decoupled: a period above the cap leaves the excess of every interval unexamined, and one below the floor makes consecutive collections overlap and re-report the same plans.

**Every setting takes effect on restart, on every database.** All binding is through `IOptions<T>` rather than `IOptionsMonitor<T>`, so nothing reloads in place: editing configuration on a running host changes nothing until it restarts. `PeriodSec` and `LeasePeriodSec` are read once, at construction, because they are handed to the timer and the lease for the life of the process; the rest are read from configuration on each tick, which makes no observable difference while the values cannot change underneath. `PeriodSec` and `LeasePeriodSec` are stored in `dbo.Parameters` but never taken from it — initialization overwrites both rows with the configured values — so a deployment behaves identically against a database it created a minute ago and one it has been running against for a year. There is no first-run state for its settings to diverge from.

One piece of pre-existing database state can still suppress collection, and it belongs to the shared lease rather than to this feature: `dbo.AcquireWatchdogLease` honours `WatchdogLeaseHolderIncludePattern` and `WatchdogLeaseHolderExcludePattern` rows in `dbo.Parameters`, plus the per-watchdog `...For<name>` variants — where the name is `QueryStoreDiagnosticsWatchdog`. A worker excluded by such a row never becomes lease holder, so its ticks skip and nothing is collected. That applies identically to every watchdog in the process and is not something this feature sets, seeds, or reads itself, but it is the one thing to check on a long-lived database when the feature is enabled and silent.

### Run window

`RunStartDate` and `RunEndDate` bound the period during which collection happens. Both are `DateTimeOffset?` and both default to `null`; the feature behaves exactly as before when neither is set. A tick collects when:

```
(RunStartDate == null || now >= RunStartDate) && (RunEndDate == null || now < RunEndDate)
```

- **`RunStartDate` is inclusive, `RunEndDate` is exclusive.** The instant that equals the end is already outside the window, so adjacent windows tile without overlapping.
- **`null` means unbounded**, not "now": an unset start collects from the first tick, an unset end collects indefinitely.
- **A start that is not before the end is an empty window** — including a start exactly equal to the end — and nothing will ever be collected. That configuration is logged as a warning once at startup naming both values, because nothing downstream will ever complain about it.

The window is evaluated inside the tick, after the configuration gate and after the lease-holder check, so a host that is outside its window still holds the lease and keeps ticking — it simply collects nothing and says so once, when the state changes.

**Set the offset explicitly.** A value without one — `2026-03-01T00:00:00` — is bound in the **host's local timezone**, which is invisible in the configured text and is rarely what was intended. Use ISO-8601 with a `Z` suffix, `2026-03-01T00:00:00Z`. As an environment variable:

```
FhirServer__Watchdog__QueryStoreDiagnostics__RunStartDate=2026-03-01T00:00:00Z
FhirServer__Watchdog__QueryStoreDiagnostics__RunEndDate=2026-03-08T00:00:00Z
```

Whenever either bound is set, the watchdog logs the effective window **converted to UTC** once at startup — before the first tick, and not repeated per tick — so an operator who typed a local time without an offset sees the resolved instant immediately rather than waiting for a window that silently opens hours late — or, for a short window, never visibly opens at all.

A **malformed** value is not silently ignored and does not silently disable the window: configuration binding throws `InvalidOperationException` naming the offending key. This matches every other typed setting in this section — `PeriodSec`, `SlowQueryCount` and `Enabled` all reject an unparseable value the same way — so a date bound introduces no failure mode the existing settings do not already have. An empty value binds as `null`, which is how a bound is removed rather than mistyped.

**The watchdog keeps ticking after `RunEndDate`; it does not shut itself down.** Outside the window it evaluates one clock comparison and returns. Self-termination is not an option available to it: completing or faulting a watchdog task makes `WatchdogsBackgroundService` cancel the token that **every** watchdog shares, so an off-by-default diagnostics feature ending its own timer would take the transaction and cleanup watchdogs with it. An hourly comparison costs nothing by contrast.

The window state is logged **only when it changes** — not open yet, open, closed — at information level. At the default hourly period a window that opens in a month would otherwise produce roughly 720 identical skip lines before collecting anything. The state a process starts in is always logged once, so the reason for silence is available immediately after a restart.

**The window boundaries take effect without a restart, but the configured values do not.** These settings bind through `IOptions<T>`, so the *values* are read once at process start and editing configuration afterwards has no effect until the host restarts — the same as every other setting in this feature. What changes without a restart is the *clock*: a window configured before the host started will open and close on schedule while the process keeps running, because each tick re-evaluates the fixed boundaries against the current time. Changing a boundary on a running host still requires a restart.

## Emitted log records

Everything this feature produces is a **structured log record**, written through the `ILogger<QueryStoreDiagnosticsWatchdog>` the host already configures. Nothing is emitted as a metric event, and there is no handler to bind: whatever a deployment already does with FHIR server logs, it does with these.

All three payload lines are emitted at **information** level. The warnings this feature raises — misconfiguration, an unavailable Query Store, a failed wait read, a plan that would not sanitize — remain at **warning** level and are unaffected by how the payload is emitted.

The payload shapes are `SlowQueryDiagnostics`, `QueryPlanDiagnostics`, and `StatisticsHealthDiagnostics`, in `Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/QueryStoreDiagnostics/Models`. They are `internal` and live beside the watchdog because they are log payload shapes, not contracts another assembly binds to.

### Why logs rather than metrics

- **Cost and blast radius.** Metric events are charged on receipt. `docs/arch/adr-2605-metric-emission-rate-limiting.md` records a high-volume emission pattern throttling a *shared* metric account and degrading monitoring for both the FHIR and DICOM services, so volume on that pipeline is an availability concern as well as a bill. Logs are the cheap place to start, and moving a data point to a metric later is easy in a way that recovering a throttled account is not.
- **The payload is not metric-shaped.** `QueryText` is unbounded free text, `SanitizedQueryPlan` is an XML document, and `TopWaitCategory` is a high-cardinality string. Those are log fields. Carrying them as metric dimensions is a cardinality incident waiting to happen.
- **Nothing here is a rate.** These are periodic snapshots meant to be read and correlated by a responder during an investigation, not aggregated into a time series.

### Slow query

One line per slow plan per tick, beginning `QueryStoreDiagnosticsWatchdog slow query.`.

Every field is its own **named** log property, so each lands as its own queryable column in Kusto or Log Analytics rather than inside a serialized blob: `QueryId`, `PlanId`, `ExecutionCount`, `TotalDurationMilliseconds`, `AverageDurationMilliseconds`, `MaxDurationMilliseconds`, `TotalCpuMilliseconds`, `AverageCpuMilliseconds`, `TotalLogicalReads`, `AverageLogicalReads`, `TotalWaitMilliseconds`, `AverageWaitMilliseconds`, `TopWaitCategory`, `WaitStatisticsStatus`, `QueryTextTruncated`, `QueryTextLength`, `IntervalStart`, `IntervalEnd`, `DiagnosticsTimestamp`, and `QueryText`.

These lines are **not** batched. At the default `SlowQueryCount` of 10 a line per row is cheap, and column-level queryability is the whole reason to emit structured logs rather than JSON documents. `QueryText` is placed last so that everything an operator scans for is readable ahead of the one unbounded field on the line.

`WaitStatisticsStatus` describes why the wait fields look the way they do: `Available` when wait statistics were read for the plan, `Unavailable` when the wait query succeeded but returned no row for it (wait capture off, or no waits accrued), and `Failed` when the wait query itself threw. Without it, "wait capture is switched off" and "the wait query has been failing for a month" are indistinguishable, because both leave the wait fields null.

`QueryId` and `PlanId` are the join keys back into Query Store, and into the `QueryStoreRuntimeStatistics` stream already exported to Log Analytics, so a responder can correlate an emitted slow query with the existing diagnostic-settings telemetry.

### Query plan

One line per reported plan per tick when `IncludeQueryPlans` is set, beginning `QueryStoreDiagnosticsWatchdog query plan.`. Named properties: `QueryId`, `PlanId`, `SanitizationStatus`, `QueryPlanTruncated`, `OriginalQueryPlanLength`, `SanitizedQueryPlanLength`, `DiagnosticsTimestamp`, and `SanitizedQueryPlan`.

These lines are **not** batched either, and for a different reason from the slow-query lines: the sanitized XML is capped at the field length rather than being small, so packing several plans into one record would risk producing a single oversized log record that the pipeline drops or truncates as a whole.

A line is emitted for unsuccessful sanitization as well, with null XML and a status of `PlanXmlUnavailable`, `InvalidXml`, or `VerificationFailed`, so a sanitization failure is observable rather than silent. That failure is *also* logged as its own warning, so systematic sanitizer breakage does not look like "plans are simply unavailable" to someone who is not reading `SanitizationStatus`.

### Statistics health

Emitted in **batches**, beginning `QueryStoreDiagnosticsWatchdog statistics health.`. Each line carries `StatisticsHealthBatchSize` rows serialized as a compact JSON array in a single log property, `StatisticsHealthRows`, alongside the pagination properties described below.

These rows are batched where the other two payloads are not because they are small, uniform, and contain no free text, so a batch of them has a predictable size. The cost of batching is that the rows arrive as a serialized blob rather than as queryable columns, which is affordable precisely because the fields are few, uniform, and cheap to re-parse; it would not be affordable for query text or plan XML.

Each row carries schema, table, and statistics name, last-updated timestamp, rows, rows sampled, modification counter, modification percentage, the auto-created / user-created / from-index / filtered flags, and the collection timestamp. The timestamp is on the row rather than only on the line so that a row stays self-describing once it is lifted out of the batch it arrived in.

When more rows are collected than fit in one batch, several lines are emitted and each one says where it sits in the set:

| Property | Meaning |
| --- | --- |
| `StatisticsHealthPage` | The **1-based** page number of this line. |
| `StatisticsHealthPageCount` | How many lines the collection was emitted across. |
| `StatisticsHealthPageRowCount` | How many rows this line carries. |
| `StatisticsHealthRowCount` | How many rows were collected in total, across every page. |
| `StatisticsHealthRows` | The rows themselves, as a compact JSON array. |

Carrying the totals on **every** line is what lets a reader distinguish a legitimately short final page from a set that was cut short by a host that died mid-collection: 18 rows on page 3 of 3 of a 58-row collection is complete, while pages 1 and 2 of 3 arriving alone is not. No line is emitted when nothing was collected — the per-tick summary already reports a count of zero, and an empty page would only make the pages harder to count.

`DiagnosticsTimestamp` on the slow-query and query-plan lines, and `Timestamp` inside each statistics row, are the moment the watchdog collected the data. They are deliberately not named `Timestamp` at the log-property level, because that name collides with the ingestion timestamp log pipelines supply for every record.

### Field size

Query text and sanitized plan XML are capped at **32 KB** per field, matching the field limit of the downstream telemetry pipeline. Values exceeding the cap are truncated and flagged (`QueryTextTruncated`, `QueryPlanTruncated`).

The pre-truncation length is reported alongside each flag so the loss is quantifiable rather than merely visible: `QueryTextLength` for query text, and `SanitizedQueryPlanLength` for plan XML. Plans additionally report `OriginalQueryPlanLength`, the raw length as read from Query Store — the two plan lengths differ by whatever sanitization removed, so reporting only the raw length would overstate how much truncation itself discarded.

Truncation is applied **after** sanitization and verification, never before, so a truncated plan can never be a partially sanitized one.

## Behaviour

### Query Store state

The watchdog reads `sys.database_query_store_options` and proceeds only when `actual_state_desc` is `READ_WRITE`. A row reporting any other state is logged as a warning naming that state together with the decoded `readonly_reason`, and the tick is skipped. When the view returns no row at all there is no state and no reason to report, so that case is logged as its own warning saying that Query Store is not configured on the database, and the tick is skipped.

**The watchdog never issues `ALTER DATABASE`.** Turning Query Store on is a database-scoped configuration change with its own permission and blast-radius considerations, and it is on by default in Azure SQL Database. Enabling it stays an explicit operator action.

### Slow-query aggregation

Runtime statistics are aggregated across every Query Store interval overlapping the lookback window, restricted to `execution_type = 0` so that only regular completed executions contribute. Averages are weighted by `count_executions` before being combined across intervals, because Query Store stores per-interval averages and an unweighted mean across intervals of unequal execution counts is wrong.

Query Store records durations and CPU in **microseconds**; the emitted contract is in milliseconds.

Results are ordered by total duration descending and limited to `SlowQueryCount`, so the reported set is the aggregate-cost hot list rather than the worst single execution.

### Self-exclusion

The watchdog's own Query Store reads are excluded by filtering out query text referencing the Query Store catalog views. An earlier iteration attempted to tag the queries with a marker comment; SQL Server does **not** preserve those comments in `query_sql_text`, so text matching on the view names is the pragmatic mechanism.

### Wait statistics

Wait statistics are collected by a **separate, best-effort** query and merged in C#. A caught `SqlException` — the wait view being unavailable, a timeout, a deadlock, or a permission denial — is logged as a warning, leaves the wait fields null, and sets `WaitStatisticsStatus` to `Failed`. Runtime results are still emitted.

`Failed` is reserved for that case: a wait read that actually broke. The ordinary outcomes of wait capture being turned off, or of no waits having accrued for a plan inside the window, are an empty result set rather than a failure — those plans carry `WaitStatisticsStatus` of `Unavailable`, with null wait fields and no warning logged.

`SqlException` is caught broadly there on purpose, so that a transient wait-query failure cannot abort the tick and suppress the runtime statistics. The trade-off accepted is that timeouts, deadlocks, permission denials and missing views are not distinguished from one another at that point; the warning carries the exception, and the slow-query line carries the status.

Wait capture is retained locally, rather than deferred entirely to the `QueryStoreWaitStatistics` Log Analytics stream, so that a single emitted slow-query line is self-contained: runtime statistics, waits, query text, and plan identity arrive together without a join against a second telemetry source.

### Statistics health

Statistics are read from `sys.stats` with an `OUTER APPLY` to `sys.dm_db_stats_properties`, so a statistics object remains visible even when its properties cannot be read. The scan is restricted to user tables and excludes temporal history tables. Rows are ordered by staleness — modification counter over row count — and limited to `StatisticsHealthCount`.

Modification percentage is left null when the row count is null or zero rather than being reported as zero, so "no data" is distinguishable from "not stale". Percentages above 100 are preserved; they are a legitimate signal that a table has churned more than its cardinality.

#### Why the count is capped

`StatisticsHealthCount` is a cap on what is *reported*, not on what is examined: the ordering runs across every qualifying statistic and only the worst are emitted. It is also the only cap on the number of rows. `StatisticsHealthBatchSize` decides how many rows share a log line, not how many rows exist, so raising it never reports more and lowering it never reports less — the two settings are easy to confuse and do quite different things.

The reason to cap is weaker than it was when each row was emitted as its own metric event, and it is worth being precise about what changed. Rows are now packed into batched log lines, so a row no longer costs a charged metric event and the per-row emission argument that `docs/arch/adr-2605-metric-emission-rate-limiting.md` supports no longer applies here in that form. What batching removes is **record count**, not bytes: 300 rows at a batch size of 20 is 15 log records instead of 300, but it is still 300 rows of ingested and retained data.

That residual volume is what the cap is for now. The schema alone defines roughly a hundred index-backed statistics across its user tables, and SQL Server adds auto-created column statistics on top of that as queries run, so the full set on a busy database is comfortably several hundred — every collection, on every database, on every host that holds the lease, for as long as the feature is enabled. Log ingestion and retention are charged by volume, and a fleet multiplies the figure by database count, so reporting everything every hour is a real ongoing cost even though it is a much smaller one than it would have been on the metrics pipeline. The second cost is readability: several hundred rows an hour of mostly healthy statistics is a stream nobody reads, which defeats the purpose of collecting them.

Capping is therefore still the right shape, and the ordering is what makes a small cap usable — the reported rows are the worst offenders rather than an arbitrary slice. Statistics with no readable row count sort last, so empty and unsampled tables do not consume the budget.

One bias is worth knowing when reading the output. Ranking is by *ratio*, so a small table that churns heavily outranks a large one that has drifted less proportionally: ten rows with a hundred modifications scores 10.0, while a hundred-million-row table with twenty million modifications scores 0.2, even though the second is far more likely to distort a plan. A handful of small, busy tables can therefore fill the report while a consequential stale statistic on a large table sits below the cut. Raising `StatisticsHealthCount` widens the window, at the volume cost described above; on logs that is a defensible thing to do for the length of an investigation, which it was not when every extra row was a metric event. If large-table staleness is what is being chased, the ordering — not the cap — is the thing to revisit.

#### Batch size is capped

`StatisticsHealthBatchSize` is clamped to 64 rows per line, with a warning naming the configured value when the clamp bites.

The reason is the same one that keeps plan XML out of a batch. Batching trades record count for record size, and a large enough batch recreates exactly the oversized record that batching plans was rejected for: a single line that a sink may truncate or reject, taking every row on it with it. A typical serialized statistics row is a little under 400 bytes, so 64 rows keeps a full page well inside the 32 KB budget the feature already applies to its other large fields.

Clamping never drops rows. A batch size above the cap simply produces more pages, and the pagination properties still account for every row. Extra lines are the cheap thing here, which is the whole reason for preferring logs in the first place.

### Failure containment

`WatchdogsBackgroundService` cancels **every** watchdog if any one of them fails. A diagnostic feature must never be able to take down transaction or cleanup watchdogs, so the collection body contains its own failures: missing views and permission denials are logged and the tick returns rather than propagating.

The missing-view handler spans the whole collection rather than each individual read, so the aborting read can be the last one, after slow queries and plans have already been emitted. Its warning is worded to hold in that case too: it reports that collection was aborted and that whatever had already been emitted was still logged, rather than claiming that nothing was collected.

That containment covers **per-tick collection**, which is where all of this feature's own collection work happens: `FhirTimer` catches whatever a tick throws and keeps ticking, so a failed collection costs one tick. The lease renewal runs on the lease's own `FhirTimer` with the same per-tick catch.

Deriving from `Watchdog<T>` does add one step outside those catches: `ExecuteAsync` awaits `InitParamsAsync`, which seeds `dbo.Parameters` and then calls this feature's `InitAdditionalParamsAsync`, *before* and *outside* the per-tick catch — so a throw there faults the watchdog task and `WatchdogsBackgroundService` cancels the rest. That exposure is shared with every other watchdog, but this feature narrows its own contribution to it: the reconciling `UPDATE` is wrapped in a catch that logs a warning and continues, and the assignments that make configuration authoritative run outside that try. A diagnostics feature is not permitted to fail the transaction and cleanup watchdogs over a row it writes only so the table reads truthfully, and collection is unaffected when the update fails.

What remains outside the catch is the period itself: `PeriodicTimer` throws on a non-positive or non-finite interval, and that throw would happen before the first tick. This is why the configured `PeriodSec` is validated at construction and replaced with the default rather than passed through, and why the run-window check skips a tick rather than ending the timer.

### Reading the primary

Every diagnostics read binds to the **primary**, not to a read-only replica, even though these are all read-only queries.

Query Store state is primary-scoped: on a secondary the database is read-only, so `sys.database_query_store_options.actual_state_desc` reports `READ_ONLY`, the `READ_WRITE` state gate rejects it, and the tick returns having emitted nothing — silently, forever, with the feature enabled and no exception raised. Replica routing is also decided per call against a process-global counter, so a single tick could otherwise check state on the primary and read data from a secondary, harvesting plan identifiers on one server and looking them up on another.

The cost is one collection per period against the primary — hourly by default — which is negligible next to the failure mode it removes.

### Configuration that disables collection

A non-positive `SlowQueryCount` or `StatisticsHealthCount` disables the corresponding section: the round-trip is skipped entirely rather than issued as a `TOP (0)` query whose empty result would be indistinguishable from a healthy one. Both cases are logged as a warning once per tick, and a section turned off deliberately through `IncludeQueryPlans` or `IncludeStatisticsHealth` is logged at information level. Misconfiguration is never fatal: a diagnostics feature must not fail the host.

A completed tick logs one information-level line carrying the collection window and the counts of slow queries, plans and statistics rows emitted, **including zeros**, so that "the watchdog has been dead for three days" is distinguishable from "there were no slow queries". The plan count is the number of plans that actually carried sanitized XML, not the number of plan lines emitted, so it is deliberately lower than the slow-query count whenever Query Store held no plan for a query or sanitization rejected one. The statistics count is the number of **rows** collected, not the number of batch lines they were emitted across.

## Sanitization

Showplan sanitization is performed **in C#** by `QueryPlanSanitizer`, not in T-SQL. The previous design did this with 236 lines of XML DML inside a stored procedure; the C# implementation is materially more reliable and easier to test with a fixture corpus.

The sanitizer:

1. parses with an `XmlReader` configured with `DtdProcessing.Prohibit` and a null resolver, so a hostile or malformed plan cannot trigger entity resolution;
2. removes every `ParameterList` element and every `ParameterCompiledValue` and `ParameterRuntimeValue` attribute, matching on local name so that Showplan namespace differences between SQL versions cannot cause a miss;
3. re-serializes without formatting;
4. **verifies** structurally — by re-walking the parsed tree after removal and checking element and attribute local names, not by scanning the serialized text — that none of those three names survive, and returns `VerificationFailed` with null XML if any do; and
5. only then truncates to the field cap.

Step 4 is defence in depth: the plan is never emitted on the strength of the removal logic alone. It is structural because Showplan embeds the original SQL in `StatementText`, so a text scan would drop — silently and permanently — any plan whose own query text happens to contain the literal string `ParameterList`.

`QueryPlanSanitizationResult` is constructed only through static factories. The factories do not re-verify the document — the success factory trusts its caller for that — but they do constrain the result's shape: every failure factory forces the XML to null, so no failure status can be paired with a payload; the success factory refuses a null document; and the truncation flag is derived from the payload rather than supplied alongside it.

### Disclosure boundary

Query text, statement text, scalar expressions, non-parameter constants, object names, missing-index recommendations, warnings, memory grants, and optimizer statistics usage are permitted diagnostic output.

This intentionally accepts that ad hoc or non-parameterized query text and plan constants may contain literal values. The protected content is parameter-value metadata in Showplan `ParameterList` elements, including compiled and runtime values.

Statistics histogram values are never read, because `range_high_key` contains actual indexed-column values.

## Security model

The watchdog runs on the FHIR server's existing SQL connection and requires no additional database principal, role, or grant. Reading the Query Store catalog views requires `VIEW DATABASE STATE`, which the service identity already holds; where it does not, the permission denial is contained and logged rather than being fatal.

Because this is an outbound emission from a process that is already trusted with the data, the previous design's audit requirements do not apply. There is no external caller to attribute, and the emitted log records are themselves the operational record.

## Repository ownership

### OSS `fhir-server`

- the watchdog, its inline SQL, and the C# sanitizer;
- the configuration class and its defaults;
- the three log payload shapes and the structured lines they are emitted on;
- unit tests for sanitization and integration tests against a live SQL Server; and
- this specification.

Nothing here is PaaS-specific, and no PaaS identity, storage account, or rollout mechanism is embedded in it. A self-hosted deployment can enable the feature and bind its own handler.

### `fhir-paas`

- routing the FHIR server log stream to Geneva or Log Analytics, and any parsing of the emitted lines built on top of it;
- setting the configuration gate per environment and ring;
- setting the collection settings, including the run window, for an investigation;
- retention, access control, and downstream handling of emitted query text and plans; and
- any responder-facing tooling built on top of the emitted stream.

### Rollout

1. Merge the OSS change. The feature ships disabled.
2. Confirm the FHIR server log stream is routed where the investigation needs it. There is no handler to bind.
3. Enable the configuration gate in a test ring and confirm emission volume and field sizes.
4. Enable it for the deployment under investigation through configuration — bounding it with `RunStartDate` and `RunEndDate` when the investigation is time-boxed — and restart that deployment for the change to take effect.

Because there is **no schema change**, there is no migration ordering dependency and no package/schema-version synchronization step. This is the single largest operational simplification relative to the rejected design. Nothing has to be written to a database to turn the feature on, so there is also no per-database state to seed, clean up, or reconcile against configuration.

## Simplifications and deferred work

Recorded deliberately; each is a candidate for a follow-up.

- **No schema version bump.** The SQL is inline in the watchdog rather than in versioned stored procedures. This removes a schema version, a 944-line migration diff, a database role, migration-sync risk, and the unresolved `dbo.LogEvent` audit-registration question. The cost is that the SQL is not independently hotfixable through a schema migration, and is reviewed as C# rather than as `.sql`. The SQL is kept in clearly formatted, commented `const` blocks to preserve readability.
- **Plans are re-emitted every tick.** There is no cross-tick deduplication by `plan_id`, so a persistently slow plan is emitted repeatedly. Accepted for v1; the duplication is bounded by `SlowQueryCount` and the tick interval, and suppression can be added once real emission volume is known.
- **Slow-query selection is total-duration only.** There is no configuration for selecting by CPU, reads, waits, or regression against a baseline. `MinDurationMilliseconds` and `SlowQueryCount` are the only tuning knobs. Richer selection is the expected first enhancement.
- **Truncation is a hard cut.** A plan exceeding 32 KB is truncated to invalid XML and flagged. It is not chunked, compressed, or externalized to blob storage. Compression would likely bring most large plans under the cap and is the obvious next step if truncation proves common.
- **No actual execution plans.** `sys.query_store_plan.query_plan` is the compile-time Showplan, equivalent to `SET SHOWPLAN_XML ON`. Actual-plan capture via `LAST_QUERY_PLAN_STATS` remains future work.
- **Plan-type and Parameter Sensitive Plan variant metadata are not read**, because those catalog fields are not stable across the supported Azure SQL fleet.
- **Statistics are reported at database level.** Incremental statistics are not expanded into partition-level rows.

## Rejected alternative: caller-invoked stored procedures

The original design exposed `dbo.GetQueryStoreSlowQueries`, `dbo.GetQueryStorePlanDiagnostics`, and `dbo.GetStatisticsHealth` behind a `FhirDiagnosticsReader` execute-only role, to be called by an external operational caller.

It was rejected because it requires an **outside principal to connect to the FHIR database and execute procedures**, which is an entirely new inbound permission model for this service. That model would have to be provisioned, granted, audited, rotated, and defended in every environment, for a diagnostic feature. The watchdog design achieves the same investigative outcome using a trust relationship that already exists.

Secondary benefits of the change:

- plan sanitization moves from T-SQL to C#, where it is more reliable and far easier to test;
- the persistent SQL surface, the database role, and the schema version all disappear; and
- results are emitted into telemetry continuously rather than requiring someone to be connected and asking at the moment the problem is happening.

## Testing requirements

### Sanitization, unit tested

1. Fixtures cover single- and multi-statement plans; compiled values; runtime values; multiple `ParameterList` elements; plans with no parameters; unusual or unknown namespaces; large and deeply nested plans; and malformed XML.
2. Fixtures contain PHI-shaped parameter values.
3. Serialized output contains no `ParameterList`, `ParameterCompiledValue`, or `ParameterRuntimeValue`.
4. Statement text, non-parameter constants, missing-index recommendations, and warnings survive unchanged.
5. Null, malformed, and verification-failing input yields the correct status and null XML.
6. Raw or partially sanitized XML is never returned.
7. Truncation sets the flag and reports the original length, and only ever occurs after successful verification.

### Collection, integration tested against live SQL

1. With Query Store enabled and a deliberately slow query executed, a slow-query line is emitted carrying a matching `QueryId`/`PlanId`.
2. A query-plan line is emitted for that plan with status `Sanitized`.
3. Statistics-health rows are emitted for user tables, batched and paginated, with page numbers and totals that account for every collected row.
4. The watchdog performs no work when the configuration gate is off.
5. Pre-existing `dbo.Parameters` rows holding stale values are reconciled to the configured values during initialization, so configuration wins on a database that already holds them.
6. A non-`READ_WRITE` Query Store state is handled without error and without emission.
7. Wait-statistic unavailability degrades to null wait fields while runtime results are still emitted, and `WaitStatisticsStatus` reports which of the three outcomes occurred.
8. The watchdog does not report its own Query Store queries.
9. Emitted content is asserted, not merely emission: the execution count matches the number of probe executions, and duration and CPU sit in a plausible millisecond range, which is what catches a regression in the weighted rollup or in the microsecond-to-millisecond conversion.

## References

- [Monitor performance by using Query Store](https://learn.microsoft.com/sql/relational-databases/performance/monitoring-performance-by-using-the-query-store)
- [How Query Store collects data](https://learn.microsoft.com/sql/relational-databases/performance/how-query-store-collects-data)
- [`sys.query_store_runtime_stats`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql)
- [`sys.query_store_wait_stats`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-wait-stats-transact-sql)
- [`sys.query_store_plan`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-plan-transact-sql)
- [`sys.query_store_query`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-query-transact-sql)
- [`sys.query_store_query_text`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-query-text-transact-sql)
- [`sys.database_query_store_options`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-database-query-store-options-transact-sql)
- [`sys.dm_db_stats_properties`](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-db-stats-properties-transact-sql)
- [Showplan XML schemas](https://schemas.microsoft.com/sqlserver/2004/07/showplan/)
