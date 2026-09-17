# ADR 2609: Limit Concurrent Searches

**Status**: Proposed
**Date**: 2026-09-17
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context

When the SQL database is oversubscribed on CPU, requests do not fail — they queue for a scheduler and take proportionally longer. Customers experience this as "the service is slow," with no signal that their own request volume is the cause.

The existing `ThrottlingMiddleware` limit is process-local, so the effective limit scales with instance count and cannot observe total database load. ADR 2504 added a database-side concurrency check for `MergeResources` and deferred HTTP 429 responses to a follow-up; this ADR covers that follow-up for the search path.

Goals: cap CPU oversubscription at ~2x, and make throttling visible as a fast 429 rather than as latency.

## Options Considered

1. **Per-instance in-flight counter** — count requests in the data layer *(rejected: process-local, scales with instance count)*
2. **`sys.dm_exec_sessions` where `status <> 'sleeping'`** — count non-idle sessions *(rejected: counts `suspended` requests blocked on locks/IO, inflating the count while CPU is idle; pool-scoped)*
3. **`sys.dm_os_schedulers` runnable queue depth** — cleanest CPU signal, MAXDOP-immune *(rejected: SQLOS-scoped, so reports pool-wide load and misattributes a neighbour's overload)*
4. **`sys.dm_db_resource_stats`** — true per-database CPU% *(rejected for enforcement: 15-second granularity is too laggy)*
5. **`sys.dm_exec_requests` where `status IN ('running','runnable')`** *(viable — chosen)*
6. **Background sampling loop with cached value** *(rejected: unnecessary once the check is inlined, and adds staleness)*

## Decision

Throttle on `COUNT(*) FROM sys.dm_exec_requests WHERE status IN ('running','runnable')` — requests on CPU or queued for it, excluding work blocked on locks and I/O. On Azure SQL this DMV is database-scoped, giving per-database attribution even inside an elastic pool, which is the decisive advantage over the SQLOS views. The check is emitted as a preamble in the same batch as the query it guards, so it adds no round trip, connection, or session, and cannot be starved by the contention it measures.

The throttling condition is:

```
throttle when X > MAX(F * C, M)

  X = COUNT(*) FROM sys.dm_exec_requests WHERE status IN ('running','runnable')
      - requests currently on CPU or queued for it, in this database
  C = cpu_limit FROM sys.dm_user_db_resource_governance WHERE database_id = DB_ID()
      - cores allocated to this database
  F = oversubscription factor, default 2.0 - the tolerated latency multiplier
  M = minimum concurrency floor, default 16 - noise guard for small SKUs
```

`C` is read live rather than cached, so the threshold tracks serverless autoscale and pool reconfiguration with no invalidation logic. Expressing the limit as a multiplier of allocated cores makes it SKU-independent; the floor prevents small SKUs from throttling on noise. `F = 2.0` is not arbitrary: under processor sharing, oversubscription equals the latency multiplier, so the threshold is stated in the units of the problem — the point where responses take twice as long as on an idle database. The check runs on a request's first command only and is suppressed inside an open transaction; a 429 delivered mid-bundle would give the client both the full latency and an error. The resulting SQL error maps to `RequestRateExceededException`, which the existing middleware already converts to a 429 with `Retry-After`, and must be excluded from `SqlRetryService`'s transient set so shed requests are not retried into more load.

## Consequences

- Overload becomes a visible, actionable 429 instead of unexplained latency — the primary goal.
- Latency degradation is bounded at ~2x, and the limit holds regardless of instance count.
- Clients that do not honour 429 see hard failures where they previously saw slow successes; ships observe-only by default so the threshold can be validated against production traffic first.
- Adds ~0.6 ms CPU per request (~15% of a 4 ms request), self-limiting since shed requests skip the work.
- The preamble must be injected centrally, moving stored-procedure calls from `CommandType.StoredProcedure` to text.
- Requires `VIEW DATABASE STATE`. Being inline, a permissions gap fails every query rather than disabling throttling, so a startup capability probe must gate it. `EXECUTE AS` a database principal drops server-scoped permissions and cannot supply the `VIEW SERVER STATE` needed on SQL Server and Managed Instance.
- The SQL load balancer, which scales a database when it is overloaded, continues to function. Throttling caps oversubscription at 2x rather than holding the database at saturation, so the elevated CPU the balancer scales on is still present and still observable — the throttle bounds overload without hiding it. Setting `F` close to 1.0 would suppress that signal and should be avoided.
- Protects SQL CPU only; pool exhaustion is assumed not to bind first.
- Where the new threshold falls below `ConcurrentRequestLimit × instance count`, it becomes the effective limit — intended, but a behaviour change for existing deployments.

## References
- ADR 2504: Limit Concurrent Calls to MergeResources Stored Procedure
- `src/Microsoft.Health.Fhir.Api/Features/Throttling/ThrottlingMiddleware.cs`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlRetry/SqlRetryService.cs`
