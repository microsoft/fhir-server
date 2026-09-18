# ADR 2609: Limit Concurrent Searches

**Status**: Proposed
**Date**: 2026-09-17
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context

When the SQL database is oversubscribed on CPU, requests do not fail — they queue for a scheduler and take proportionally longer. Customers experience this as "the service is slow," with no signal that their own requests are the cause.

The existing `ThrottlingMiddleware` limit is process-local, so the effective limit scales with instance count and cannot observe total database load. The `MergeResources` throttling from ADR 2504 is a separate, write-path mechanism and is not replaced by this one; the two remain in effect independently. This ADR applies a CPU-based limit to the search path.

Goals: cap CPU oversubscription at ~2x, and make throttling visible as a fast 429 rather than as latency only.

## Options Considered

1. **Per-instance in-flight counter** — count requests in the data layer *(rejected: process-local, scales with instance count)*
2. **`sys.dm_exec_sessions` where `status <> 'sleeping'` and `database_id = db_id()`** — count non-idle sessions *(rejected: counts `suspended` requests blocked on locks/IO, inflating the count while CPU is idle)*
3. **`sys.dm_os_schedulers` runnable queue depth** — cleanest CPU signal, MAXDOP-immune *(rejected: SQLOS-scoped, so reports pool-wide load and misattributes a neighbour's overload)*
4. **`sys.dm_db_resource_stats`** — true per-database CPU% *(rejected for enforcement: 15-second granularity is too laggy)*
5. **`sys.dm_exec_requests` where `status IN ('running','runnable')`** *(viable — chosen)*
6. **Background sampling loop with cached value** *(rejected: unnecessary once the check is inlined, and adds staleness)*

## Decision

Throttle on `count(*) FROM sys.dm_exec_requests WHERE status IN ('running','runnable')` — requests on CPU or queued for it, excluding work blocked on locks and I/O. On Azure SQL this DMV is database-scoped, giving per-database attribution even inside an elastic pool, which is the decisive advantage over the SQLOS views. The check is emitted as the first statement in the same batch as the query it guards, so it adds no round trip, connection, or session, and cannot be starved by the contention it measures.

The throttling condition is:

```
throttle when X > max(F * C, M)

  X = count(*) FROM sys.dm_exec_requests WHERE status IN ('running','runnable')
      - requests currently on CPU or queued for it, in this database
  C = cpu_limit FROM sys.dm_user_db_resource_governance WHERE database_id = db_id()
      - cores allocated to this database
  F = oversubscription factor, default 2.0 - the tolerated latency multiplier
  M = minimum concurrency floor, default 16 - noise guard for small SKUs
```

`C` is read live rather than cached, so the threshold tracks serverless autoscale and pool reconfiguration with no invalidation logic. Expressing the limit as a multiplier of allocated cores makes it SKU-independent; the floor prevents small SKUs from throttling on noise. `F = 2.0` is not arbitrary: under processor sharing, oversubscription equals the latency multiplier, so the threshold is stated in the units of the problem — the point where responses take twice as long as on an idle database. The resulting SQL error maps to `RequestRateExceededException`, which the existing middleware already converts to a 429 with `Retry-After`.

## Consequences

- Overload becomes a visible, actionable 429 instead of unexplained latency — the primary goal.
- Latency degradation is bounded at ~2x, and the limit holds regardless of instance count.
- Clients that do not honour 429 see hard failures where they previously saw slow successes.
- Adds ~0.6 ms CPU per request (~15% of a 4 ms request), self-limiting since shed requests skip the work.
- Access to the DMV is obtained via `EXECUTE AS 'dbo'` rather than granting `VIEW DATABASE STATE`. This works on Azure SQL, where the DMV needs only database-scoped permission. On SQL Server and Managed Instance it requires server-scoped `VIEW SERVER STATE`, which database-level impersonation cannot supply, so throttling is a no-op there.
- The SQL load balancer, which scales a database when it is overloaded, continues to function. Throttling caps oversubscription at 2x rather than holding the database at saturation, so the elevated CPU the balancer scales on is still present and still observable — the throttle bounds overload without hiding it. Setting `F` close to 1.0 would suppress that signal and should be avoided.
- Where the new threshold falls below `ConcurrentRequestLimit × instance count`, it becomes the effective limit — intended, but a behaviour change for existing deployments.

## References
- ADR 2504: Limit Concurrent Calls to MergeResources Stored Procedure
- `src/Microsoft.Health.Fhir.Api/Features/Throttling/ThrottlingMiddleware.cs`
