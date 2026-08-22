# ADR-2608: Query Store Performance Diagnostics Collection

Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

**Status**: Proposed
**Date**: 2026-08-22
**Feature**: Query Store performance diagnostics

## Context

Diagnosing a slow FHIR service today requires Azure SQL Query Store data — the slowest recent queries, their execution plans, wait statistics, and statistics health. None of that reaches an on-call engineer or an automated SRE agent without a human who holds database credentials connecting to the customer's data plane and querying it by hand. That is slow during an incident and does not scale across a multi-tenant fleet.

The constraints that shaped the decision are as much operational as technical. The data plane holds PHI, and Query Store plan XML can embed literal parameter values, so anything that moves plans out of the database is a privacy boundary. The team deliberately operates the service without standing database access, so any design that requires a new identity with rights on customer databases is not merely an implementation detail — it is a permission model the organisation would have to review, provision per environment, rotate, and audit for as long as the feature exists.

## Options Considered

1. **External caller executing diagnostics stored procedures** — grant an outside identity, such as the SRE agent, a SQL role restricted to a fixed set of diagnostics stored procedures, and let it connect to the customer database directly. *(rejected: creates a new standing access path into the data plane)*
2. **Geneva Actions brokering the same stored procedures** — keep the procedures, but invoke them through Geneva Actions so the agent never holds SQL credentials itself. *(rejected: moves the credential but does not remove the access path)*
3. **In-process background job that emits diagnostics** — the server collects on its own schedule using the identity it already holds, sanitises plans in C#, and publishes the results through the existing metrics notification pipeline. *(chosen)*

## Decision

We chose option 3: a watchdog-style background job inside the FHIR server, off by default, enabled and tuned entirely through configuration.

The deciding argument is that **it introduces no new access path**. The server is already authenticated to its own database and already runs scheduled background work there; diagnostics collection is additional work on an existing connection and an existing identity. Options 1 and 2 both require a principal that can reach the data plane from outside. Option 2 is genuinely better than option 1 — the agent never holds a credential — but Geneva Actions is a layer in front of the access path, not a replacement for it: the role, its grants, and its lifecycle still have to exist. A SQL reviewer on the team made the same point, that an outside force connecting in and running procedures is a permission model we do not otherwise operate.

Three consequences of that choice reinforced it. Data now leaves by **push through the existing notification pipeline** that hosts already bind to, rather than by an inbound query into the data plane, which keeps the direction of trust unchanged. **Plan sanitisation moves from T-SQL into C#**, where it is unit-testable, namespace-agnostic across SQL versions, and fails closed by verifying its own output before publishing. And enablement uses the **existing configuration surface**, so switching diagnostics on in an environment is an ordinary deployment change rather than a database operation.

We further decided that **all settings live in configuration and none in `dbo.Parameters`**. The first iteration followed the existing watchdog convention of keeping runtime knobs in that table, which meant an operator had to run an `UPDATE` to arm the feature and meant the period was read from the database over the top of configuration. That reintroduces database writes for a feature whose purpose is to avoid needing database access, and splits the control surface in two. Honouring it required this watchdog to stop deriving from the shared `Watchdog<T>` base class, whose initialisation privately writes its period rows and reads them back; the distributed lease that prevents duplicate collection across replicas was kept.

## Consequences

### Benefits

- No new principal, role, firewall exception, or credential to provision, rotate, or audit; nothing outside the service gains data-plane access.
- Diagnostics are enabled and tuned per environment through normal configuration, including an optional run window, and are off by default.
- The PHI boundary is enforced in C#, is covered by unit tests, and fails closed rather than emitting an unverified plan.
- Collection is single-instance through the existing lease, so notifications are not multiplied by replica count.

### Adverse effects

- Diagnostics cannot be pulled on demand. Data appears on the collection period — hourly by default — so an incident is served by data already being collected, not by an engineer asking a question and getting an immediate answer. A run window has to be configured in advance.
- Settings bind through `IOptions<T>`, so changing them on a running host requires a restart.
- This watchdog no longer shares the `Watchdog<T>` base class and therefore re-implements its timer and lease orchestration and will not inherit future improvements to it. That divergence is the cost of removing `dbo.Parameters`, and should be revisited if the base class itself moves to configuration.
- One shared type changed: `WatchdogLease<T>` was constrained to `T : Watchdog<T>` while using its type argument solely for `typeof(T).Name`. The constraint was relaxed so a self-scheduling component can still elect a single replica. It restricted nothing the class used, and every existing caller passes a `Watchdog<T>` and is unaffected, but it is a shared-file change and reviewers should confirm they are comfortable with it.
- Collection depends on Query Store being enabled and in `READ_WRITE` state on the database; otherwise the job reports why it cannot collect and does nothing.

### Neutral effects

- The emitted notifications are contracts that hosts bind to; this repository prescribes no sink.
- The lease continues to write to its own table. That is runtime coordination rather than configuration, and is not part of what this decision removed.

## References

- PR [#5723](https://github.com/microsoft/fhir-server/pull/5723)
- `docs/QueryStorePerformanceDiagnostics.md` — design and configuration reference
- `docs/arch/adr-2602-database-logging.md` — precedent for diagnostics gathered inside the service
- `docs/arch/adr-2605-metric-emission-rate-limiting.md` — precedent for emission-rate concerns on the metrics pipeline
