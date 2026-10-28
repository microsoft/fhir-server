# ADR-2608: Query Store Performance Diagnostics Collection

Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

**Status**: Proposed
**Date**: 2026-08-22
**Feature**: Query Store performance diagnostics

## Context

Diagnosing a slow FHIR service today requires Azure SQL Query Store data: the slowest recent queries, their execution plans, wait statistics, and statistics health. Getting that data means connecting to the database, copying and pasting queries, and working by hand against a production system. That is slow and prone to errors.

Direct database access can also expose PHI or PII by accident. The risk is not only the resource data itself. Query Store captures compiled and runtime parameter values inside query plans, so a plan copied out of the database can carry patient data with it even when nobody queried a resource table.

We look to simplify database performance analysis and reduce the possible PHI / PII exposure as part of this ADR.

## Options Considered

1. **External caller executing diagnostics stored procedures** — grant an outside identity, such as the cloud SRE agent, a SQL role restricted to a fixed set of diagnostics stored procedures, and let it connect to the customer database directly. *(rejected: creates a new standing access path into the data plane)*
2. **PaaS Action layer brokering the same stored procedures** — keep the procedures, but invoke them through the existing PaaS Action broker so the agent never holds SQL credentials itself. *(rejected: moves the credential but does not remove the access path)*
3. **In-process background job that emits diagnostics** — the server collects on its own schedule using the identity it already holds, sanitizes plans in C#, and writes the results out as structured logs. *(chosen)*

## Decision

We chose option 3: a watchdog-style background job inside the FHIR server, off by default, enabled and tuned entirely through configuration.

The deciding argument is that it introduces no new inbound SQL access path. The server is already authenticated to its own database and already runs scheduled background work there, so diagnostics collection is more work on a connection and an identity that both already exist. Options 1 and 2 each need a principal that can reach the data plane from outside. Option 2 is better than option 1, because the agent never holds a credential itself, but the existing action path has no database access today. Choosing it would still mean opening a path that is not there now.

Three things follow from that choice, and each of them reinforced it.

Data leaves by push, as structured log records on the logger the server already writes to, rather than by an inbound query into the data plane. This can expose sensitive diagnostic data to log readers who lack database access, so the destination, access controls, retention, and auditing require review.

Plan sanitization moves from T-SQL into C#. It removes and verifies the absence of the specified Showplan parameter metadata by element and attribute name, independent of the Showplan namespace. Invalid XML and failed verification produce no plan XML. This is not a general PHI or PII scrubber: query text, statement text, other literals, and object names can still contain sensitive information, including values from parameterized queries represented as constants in a plan.

Enablement uses the existing configuration surface, so turning diagnostics on in an environment is an ordinary deployment change rather than a database operation.

The watchdog calls five versioned stored procedures using the existing service identity. This replaces the earlier inline SQL design to follow the repository's stored-procedure convention. Separate procedures preserve independent retries and keep a wait-statistics failure from suppressing slow-query records. The tradeoff is a schema migration and a rollout dependency. Schema version 120 installs them. On an older or unknown schema version, collection is skipped and retried on the next tick. A deployment must verify that the service identity can execute the procedures and read the required Query Store and statistics views.

Collection has a configurable cooperative deadline, and the optional wait read has a shorter independent deadline by default. Each SQL attempt uses a five-second command timeout. Runtime records are retained when the wait read times out, but cancellation cannot guarantee an immediate server-side stop or fence an expired lease. These proposed defaults require validation on a representative workload before enablement.

We also decided to emit the diagnostics as logs rather than as metric events. Three reasons, in order of weight.

Metric events are charged when they are received, and the volume here is not small. `docs/arch/adr-2605-metric-emission-rate-limiting.md` records a high volume emission pattern that throttled a shared metric account and degraded monitoring for both the FHIR and DICOM services. That is an availability problem as well as a bill, and it is the kind of problem that is easier to avoid than to recover from.

The data was never metric shaped. Query text is unbounded free text, a sanitized plan is an XML document, and the top wait category is a high cardinality string. Those belong in log fields. Putting them in metric dimensions invites a cardinality incident. Nothing collected here is a rate either. These are periodic snapshots that a responder reads during an investigation.

Logs are also the cheap place to start. If a specific number later turns out to be worth alerting on, promoting it to a metric is a small change. Recovering a throttled metric account is not.

The statistics health rows are batched as a JSON array. Each line carries its page number, page count, and total row count, so a reader can detect a missing page. The row-count cap does not guarantee a byte limit. Long identifiers can still produce an oversized record. Slow query and plan records are not batched; their named properties remain subject to the destination's mapping and size limits.

We also decided that every setting is set in configuration, and that configuration always wins over `dbo.Parameters`. The first iteration followed the existing watchdog convention of keeping runtime values in that table. That meant an operator had to run an `UPDATE` to arm the feature, and the collection period was read back from the database over the top of the configured value. Both work against the goal: a feature whose purpose is to remove the need for database access should not require a write to the database to switch on, and configuration that is silently overridden by a stored row is not really configuration.

The watchdog uses the shared `AllowDbPeriodOverride` switch to keep configuration authoritative without reading or writing its period rows in `dbo.Parameters`. Existing rows, if any, remain untouched. The watchdog still uses the shared timer and distributed lease.

## Consequences

### Benefits

- No new principal, role, firewall exception, or credential to provision, rotate, or audit. Nothing outside the service gains data-plane access.
- Diagnostics are enabled and tuned per environment through normal configuration, including an optional run window, and are off by default.
- Parameter metadata removal is covered by unit tests, and a plan that fails verification is not emitted.
- The existing lease normally coordinates collection across replicas.
- The watchdog derives from `Watchdog<T>` and uses the shared timer and lease orchestration.
- Emission costs a log record rather than a charged metric event, so enabling the feature does not add load to the metrics pipeline.

### Adverse effects

- Diagnostics cannot be pulled on demand. Data appears on the collection period, hourly by default, so an incident is served by data that was already being collected rather than by an engineer asking a question and getting an answer straight away. A run window has to be configured in advance.
- Query text and sanitized plans can still contain sensitive literals or object names. A deployment must approve the destination, access controls, and retention before enabling collection.
- The lease does not fence an in-progress collection. Lease loss can lead to overlapping collectors and duplicate log records.
- Settings bind through `IOptions<T>`, so changing them on a running host requires a restart.
- Logs are not aggregated for you. Nothing here arrives as a pre-computed time series, so trend questions need a query over the emitted lines rather than a metric chart.
- Stale period rows from an earlier version remain in `dbo.Parameters`, but the watchdog ignores them. Configuration is the source of the active periods.
- Collection depends on Query Store being enabled and in `READ_WRITE` state on the database. Otherwise the job reports why it cannot collect and does nothing.
- Collection also requires schema version 120 and the required SQL permissions. Earlier supported schemas continue to run the service, but do not collect these diagnostics.
- One piece of pre-existing database state can still suppress collection silently. `dbo.AcquireWatchdogLease` honors watchdog lease include and exclude patterns held in `dbo.Parameters`. A worker excluded by such a row never becomes lease holder, so the feature can be enabled and stay quiet. This applies to every watchdog in the process and is not something this feature sets or reads, but it is the first thing to check on a long-lived database.

### Neutral effects

- The emitted lines are ordinary log records. There is no handler to bind and this repository prescribes no sink. Whatever a deployment already does with FHIR server logs, it does with these.
- The statistics health rows arrive as a JSON array inside one log property rather than as separate columns. A reader has to parse them. That is the trade accepted for batching, and it is affordable because the fields are few and uniform.
- The lease continues to write to its own table. That is runtime coordination rather than configuration, and is not part of what this decision changed.

## References

- PR [#5723](https://github.com/microsoft/fhir-server/pull/5723)
- `docs/QueryStorePerformanceDiagnostics.md` — design and configuration reference
- `docs/arch/adr-2602-database-logging.md` — precedent for diagnostics gathered inside the service
- `docs/arch/adr-2605-metric-emission-rate-limiting.md` — the emission-rate incident behind the choice of logs over metrics
