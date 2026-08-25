# Query Store Performance Diagnostics - Baseline Specification

## Status

Agreed baseline for implementation. This document defines an opt-in background worker that emits Azure SQL Query Store diagnostics from inside the FHIR server, the enablement model, the emitted contracts, the disclosure boundary, and the repository ownership split.

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

A **watchdog** — the repository's existing leased background-worker pattern — periodically reads Query Store and statistics metadata and **pushes** the results out as metrics notifications. It runs inside the FHIR server process on the server's **existing** SQL identity.

```text
FHIR server instance (lease holder)
  └── QueryStoreDiagnosticsWatchdog        every PeriodSec, default 3600s
        ├── sys.database_query_store_options      state gate, always primary
        ├── sys.query_store_*                     slow plans + query text
        ├── sys.query_store_wait_stats            best effort
        ├── sys.stats / sys.dm_db_stats_properties
        └── QueryPlanSanitizer (C#)               strips parameter values
              │
              └── IMediator.PublishAsync(IMetricsNotification)
                    └── host-supplied handler (PaaS -> Geneva / Log Analytics)
```

The critical property is that **nothing new connects inbound to the database**. The work is done by the service that already holds a connection, so the feature introduces no new authentication path, no new database principal, and no new grant.

### Why a watchdog

`WatchdogsBackgroundService` and `WatchdogLease<T>` already provide what this feature needs, and each of these would otherwise have to be reinvented:

- **Single-runner election.** `WatchdogLease<T>` ensures exactly one instance in a multi-instance deployment performs the work, so an eight-instance service does not issue eight concurrent Query Store scans and emit eight copies of every notification. The lease is invisible runtime coordination rather than configuration: it holds nothing an operator sets, and it lives in `dbo.WatchdogLeases` behind `dbo.AcquireWatchdogLease`, shared with every other watchdog.
- **A managed background timer.** `FhirTimer` supplies the randomized start-up stagger that keeps replicas from collecting on the same second, and catches whatever a tick throws so that a failed collection costs one tick rather than the process.
- **A precedent for emitting SQL telemetry.** `GeoReplicationLagWatchdog` reads a SQL view on a timer and publishes an `IMetricsNotification`; the host binds a handler that forwards it. This feature is the same shape.

**It deliberately does not derive from `Watchdog<T>`.** That base class inserts `{Name}.PeriodSec` and `{Name}.LeasePeriodSec` into `dbo.Parameters` on every start and then reads the period back **over** the configured value, from a private, non-virtual initialization step with no override hook. This feature is configured exclusively from configuration and writes nothing to the database, so it owns a `FhirTimer` and a `WatchdogLease<T>` directly and reproduces the rest of what the base class did — the lease-holder gate, the capped randomized stagger, and the per-tick timing line — in its own `ExecuteAsync` and tick handler. `WatchdogLease<T>` uses its type argument only to derive the lease resource name from `typeof(T).Name`; its former `T : Watchdog<T>` constraint restricted nothing it actually used, so it was relaxed to admit a self-scheduling component. Every other caller passes a `Watchdog<T>` and is unaffected, and the timer and base class are used unmodified.

### Enablement

The feature is **off by default** and is gated by one switch, in configuration.

| Gate | Location | Purpose |
| --- | --- | --- |
| `FhirServer:Watchdog:QueryStoreDiagnostics:Enabled` | Host configuration | When false the watchdog is never started by `WatchdogsBackgroundService`, and no Query Store read occurs. |

**All configuration for this feature lives in configuration, and the feature writes none of it to the database.** There is no row to seed, arm, or update: no `IsEnabled` row, no `PeriodSec` row, no `LeasePeriodSec` row. Turning the feature on, tuning it, and turning it off are configuration changes plus a restart — no `UPDATE` against a live database, and no possibility of a database holding a value that disagrees with the deployment's configuration.

The watchdog also re-reads `Enabled` at the start of every tick and returns without collecting when it is false. That check cannot be reached with the value false as the host is wired today: `WatchdogsBackgroundService` gates startup on the same configuration snapshot, and `IOptions<T>` does not reload in place. It is kept because the watchdog is registered `AsSelf` and this is the only place the opt-out is enforced at the unit of work, so a future call site that executes a collection directly cannot bypass it. The cost is one boolean read per period.

The only database row this feature causes to exist is its **lease**, in `dbo.WatchdogLeases` through `dbo.AcquireWatchdogLease`. That is what stops every replica collecting and emitting the same diagnostics every period; it holds no configuration and is the same mechanism every other watchdog uses. That shared stored procedure does consult `dbo.Parameters` for the fleet-wide watchdog lease-holder include and exclude patterns, which is noted under [Configuration](#configuration): it is shared framework behaviour that this feature neither sets nor reads for itself.

### Configuration

`WatchdogConfiguration.QueryStoreDiagnostics`, bound from `FhirServer:Watchdog:QueryStoreDiagnostics`. Note that `Watchdog` is a sibling of `Operations` under `FhirServer`, not nested inside it:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | The gate described above. |
| `PeriodSec` | `3600` | Interval between collections, and — clamped to `[60, 86400]` seconds — the Query Store lookback window each collection covers. A non-positive or non-finite value is rejected with a warning and the `3600` default is used, because `PeriodicTimer` would otherwise throw and fault every watchdog in the process. |
| `SlowQueryCount` | `10` | Number of slow plans to report per tick. |
| `MinDurationMilliseconds` | `1000` | Minimum weighted average duration for a plan to be reported. A negative value is treated as `0` — which makes every query qualify — and warns. |
| `IncludeQueryPlans` | `true` | Whether sanitized Showplan XML is emitted. |
| `IncludeStatisticsHealth` | `true` | Whether statistics metadata is emitted. |
| `StatisticsHealthCount` | `20` | Worst-ranked statistics rows to report per tick. Each row is one notification, so this directly multiplies emission volume; see [Why the count is capped](#why-the-count-is-capped). |
| `RunStartDate` | `null` (unset) | Inclusive start of the optional run window. Ticks before it collect nothing. `null` means no lower bound. |
| `RunEndDate` | `null` (unset) | **Exclusive** end of the optional run window. Ticks at or after it collect nothing. `null` means no upper bound. |

The lookback window is `PeriodSec` clamped to `[60, 86400]` seconds, so the collection window tracks the collection interval and a misconfigured value cannot request an unbounded scan.

The watchdog warns on every tick where the clamp changed the value, naming the effective window and what the clamp costs, and pointing at `FhirServer:Watchdog:QueryStoreDiagnostics:PeriodSec` as the thing to change. The tick interval itself is **not** clamped, so whenever the clamp bites the interval and the window it covers are permanently decoupled: a period above the cap leaves the excess of every interval unexamined, and one below the floor makes consecutive collections overlap and re-report the same plans.

**Every setting takes effect on restart, on every database.** All binding is through `IOptions<T>` rather than `IOptionsMonitor<T>`, so nothing reloads in place: editing configuration on a running host changes nothing until it restarts. `PeriodSec` is read once, at construction, because it is handed to the timer for the life of the process; the rest are read from configuration on each tick, which makes no observable difference while the values cannot change underneath. None of them is read from or written to the database, so a deployment behaves identically against a database it created a minute ago and one it has been running against for a year — there is no first-run state for its settings to diverge from.

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

## Emitted contracts

Three notification types implement `IMetricsNotification`, each reporting `FhirOperation` `query-store-diagnostics` and `ResourceType` `System`. Hosts bind handlers to route them; the OSS repository does not prescribe a sink.

### `SlowQueryNotification`

One per slow plan per tick. Carries `QueryId`, `PlanId`, execution count, total/average/maximum duration, total/average CPU, total/average logical reads, total/average wait time, top wait category, `WaitStatisticsStatus`, the Query Store query text, and the collection window bounds.

`WaitStatisticsStatus` describes why the wait fields look the way they do: `Available` when wait statistics were read for the plan, `Unavailable` when the wait query succeeded but returned no row for it (wait capture off, or no waits accrued), and `Failed` when the wait query itself threw. Without it, "wait capture is switched off" and "the wait query has been failing for a month" are indistinguishable, because both leave the wait fields null.

`QueryId` and `PlanId` are the join keys back into Query Store, and into the `QueryStoreRuntimeStatistics` stream already exported to Log Analytics, so a responder can correlate an emitted slow query with the existing diagnostic-settings telemetry.

### `QueryPlanNotification`

One per reported plan per tick when `IncludeQueryPlans` is set. Carries `QueryId`, `PlanId`, the sanitized Showplan XML, a truncation flag, the raw and sanitized plan lengths, and a sanitization status.

Notifications are emitted for unsuccessful sanitization as well, with null XML and a status of `PlanXmlUnavailable`, `InvalidXml`, or `VerificationFailed`, so a sanitization failure is observable rather than silent.

### `StatisticsHealthNotification`

One per statistics object per tick when `IncludeStatisticsHealth` is set. Carries schema, table, and statistics name, last-updated timestamp, rows, rows sampled, modification counter, modification percentage, and the auto-created / user-created / from-index / filtered flags.

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

`SqlException` is caught broadly there on purpose, so that a transient wait-query failure cannot abort the tick and suppress the runtime metrics. The trade-off accepted is that timeouts, deadlocks, permission denials and missing views are not distinguished from one another at that point; the warning carries the exception, and the notification carries the status.

Wait capture is retained locally, rather than deferred entirely to the `QueryStoreWaitStatistics` Log Analytics stream, so that a single emitted slow-query record is self-contained: runtime metrics, waits, query text, and plan identity arrive together without a join against a second telemetry source.

### Statistics health

Statistics are read from `sys.stats` with an `OUTER APPLY` to `sys.dm_db_stats_properties`, so a statistics object remains visible even when its properties cannot be read. The scan is restricted to user tables and excludes temporal history tables. Rows are ordered by staleness — modification counter over row count — and limited to `StatisticsHealthCount`.

Modification percentage is left null when the row count is null or zero rather than being reported as zero, so "no data" is distinguishable from "not stale". Percentages above 100 are preserved; they are a legitimate signal that a table has churned more than its cardinality.

#### Why the count is capped

`StatisticsHealthCount` is a cap on what is *reported*, not on what is examined: the ordering runs across every qualifying statistic and only the worst are emitted. The cap matters because each reported row is published as its own `StatisticsHealthNotification`, so the setting is a direct multiplier on emission volume — per collection, per database, per host.

The schema alone defines roughly a hundred index-backed statistics across its user tables, and SQL Server adds auto-created column statistics on top of that as queries run, so the full set on a busy database is comfortably several hundred. Reporting all of them would turn one collection into several hundred notifications, and a fleet multiplies that by database count. `docs/arch/adr-2605-metric-emission-rate-limiting.md` records what that costs: a high-volume emission pattern throttled a *shared* metric account and degraded monitoring for both the FHIR and DICOM services. Metric events are charged on receipt, so volume is both a cost and an availability concern.

Capping is therefore the right shape, and the ordering is what makes a small cap usable — the reported rows are the worst offenders rather than an arbitrary slice. Statistics with no readable row count sort last, so empty and unsampled tables do not consume the budget.

One bias is worth knowing when reading the output. Ranking is by *ratio*, so a small table that churns heavily outranks a large one that has drifted less proportionally: ten rows with a hundred modifications scores 10.0, while a hundred-million-row table with twenty million modifications scores 0.2, even though the second is far more likely to distort a plan. A handful of small, busy tables can therefore fill the report while a consequential stale statistic on a large table sits below the cut. Raising `StatisticsHealthCount` widens the window, at the emission cost described above. If large-table staleness is what is being chased, the ordering — not the cap — is the thing to revisit.

### Failure containment

`WatchdogsBackgroundService` cancels **every** watchdog if any one of them fails. A diagnostic feature must never be able to take down transaction or cleanup watchdogs, so the collection body contains its own failures: missing views and permission denials are logged and the tick returns rather than propagating.

The missing-view handler spans the whole collection rather than each individual read, so the aborting read can be the last one, after slow queries and plans have already been published. Its warning is worded to hold in that case too: it reports that collection was aborted and that whatever had already been emitted was still published, rather than claiming that nothing was collected.

That containment covers **per-tick collection**, which is where all of this feature's own database work happens: `FhirTimer` catches whatever a tick throws and keeps ticking, so a failed collection costs one tick. The lease renewal is the only other database call, and it runs on the lease's own `FhirTimer` with the same per-tick catch. There is no initialization step left to fail outside either catch. Watchdogs that derive from `Watchdog<T>` do have one — `ExecuteAsync` awaits `InitParamsAsync`, which seeds `dbo.Parameters`, *before* and *outside* the per-tick catch, so a throw there faults the watchdog task and `WatchdogsBackgroundService` cancels the rest — and not deriving from it removes that failure mode here along with the writes.

What remains outside the catch is the period itself: `PeriodicTimer` throws on a non-positive or non-finite interval, and that throw would happen before the first tick. This is why the configured `PeriodSec` is validated at construction and replaced with the default rather than passed through, and why the run-window check skips a tick rather than ending the timer.

### Reading the primary

Every diagnostics read binds to the **primary**, not to a read-only replica, even though these are all read-only queries.

Query Store state is primary-scoped: on a secondary the database is read-only, so `sys.database_query_store_options.actual_state_desc` reports `READ_ONLY`, the `READ_WRITE` state gate rejects it, and the tick returns having emitted nothing — silently, forever, with the feature enabled and no exception raised. Replica routing is also decided per call against a process-global counter, so a single tick could otherwise check state on the primary and read data from a secondary, harvesting plan identifiers on one server and looking them up on another.

The cost is one collection per period against the primary — hourly by default — which is negligible next to the failure mode it removes.

### Configuration that disables collection

A non-positive `SlowQueryCount` or `StatisticsHealthCount` disables the corresponding section: the round-trip is skipped entirely rather than issued as a `TOP (0)` query whose empty result would be indistinguishable from a healthy one. Both cases are logged as a warning once per tick, and a section turned off deliberately through `IncludeQueryPlans` or `IncludeStatisticsHealth` is logged at information level. Misconfiguration is never fatal: a diagnostics feature must not fail the host.

A completed tick logs one information-level line carrying the collection window and the counts of slow queries, plans and statistics rows published, **including zeros**, so that "the watchdog has been dead for three days" is distinguishable from "there were no slow queries". The plan count is the number of plans that actually carried sanitized XML, not the number of plan notifications published, so it is deliberately lower than the slow-query count whenever Query Store held no plan for a query or sanitization rejected one.

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

Because this is an outbound push from a process that is already trusted with the data, the previous design's audit requirements do not apply. There is no external caller to attribute, and the emitted notifications are themselves the operational record.

## Repository ownership

### OSS `fhir-server`

- the watchdog, its inline SQL, and the C# sanitizer;
- the configuration class and its defaults;
- the three notification contracts;
- unit tests for sanitization and integration tests against a live SQL Server; and
- this specification.

Nothing here is PaaS-specific, and no PaaS identity, storage account, or rollout mechanism is embedded in it. A self-hosted deployment can enable the feature and bind its own handler.

### `fhir-paas`

- binding notification handlers and routing the emissions to Geneva or Log Analytics;
- setting the configuration gate per environment and ring;
- setting the collection settings, including the run window, for an investigation;
- retention, access control, and downstream handling of emitted query text and plans; and
- any responder-facing tooling built on top of the emitted stream.

### Rollout

1. Merge the OSS change. The feature ships disabled.
2. Bind a handler and configure routing in `fhir-paas`.
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
- results are pushed into telemetry continuously rather than requiring someone to be connected and asking at the moment the problem is happening.

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

1. With Query Store enabled and a deliberately slow query executed, a `SlowQueryNotification` is emitted carrying a matching `QueryId`/`PlanId`.
2. A `QueryPlanNotification` is emitted for that plan with status `Sanitized`.
3. `StatisticsHealthNotification` rows are emitted for user tables.
4. The watchdog performs no work when the configuration gate is off, and starting it creates no `dbo.Parameters` row and reads none.
5. A non-`READ_WRITE` Query Store state is handled without error and without emission.
6. Wait-statistic unavailability degrades to null wait fields while runtime results are still emitted, and `WaitStatisticsStatus` reports which of the three outcomes occurred.
7. The watchdog does not report its own Query Store queries.
8. Emitted content is asserted, not merely emission: the execution count matches the number of probe executions, and duration and CPU sit in a plausible millisecond range, which is what catches a regression in the weighted rollup or in the microsecond-to-millisecond conversion.

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
