# ADR-2611: Enable SQL Server Defragmentation

**Status**: Proposed
**Date**: 2026-11-06
**Feature**: Enable SQL Server defragmentation

## Context

SQL Server index defragmentation is implemented by `DefragWatchdog` but remains opt-in. Defragmentation can reclaim substantial database space and reduce fragmentation, including after heavy imports that create fragmentation even when resources have a single version and have not been updated or deleted. In one MedTech deployment, **in-place reorganization** reduced a database that had grown close to 4 TB to less than 200 GB. Separately, **re-indexing** after heavy import reduced fragmentation by up to 30%, even when resources had a single version and no updates or deletes. These are observed outcomes, not guaranteed results for every database.

Enabling the capability safely also requires avoiding unacceptable impact to application queries. The `DefragBlocking` integration test demonstrates that `ALTER INDEX ... REORGANIZE` can hold a schema-stability lock while `UPDATE STATISTICS ... WITH FULLSCAN` waits for a schema-modification lock; a subsequent `SELECT` may then queue behind the statistics request. This is one demonstrated case: other application reads or writes that join a lock chain attributable to defrag can also be delayed. The test harness currently cancels defrag to let statistics and reads proceed, but the watchdog does not yet provide this automatic protection.

## Defragmentation Strategies

The SQL implementation supports two strategies, selected by `Defrag.IndexRebuild.IsEnabled`. **In-place reorganization** (`ALTER INDEX ... REORGANIZE`) incrementally reorganizes leaf pages without recreating the index. It is the current default and generally has lower space and resource requirements, but may not provide the same level of compaction as rebuilding. **Index rebuild** (`ALTER INDEX ... REBUILD`) recreates the index and can provide more complete compaction and refresh index statistics, but can consume more CPU, I/O, and temporary disk space. The current rebuild procedure requests `ONLINE = ON`; this reduces, but does not eliminate, operational impact and does not guarantee absence of blocking. For supported SQL Server versions, rebuilds can additionally use resumable online index operations (`RESUMABLE = ON`). A new database parameter, `Defrag.IndexRebuild.IsResumable`, will control this behavior and default to off to preserve current behavior. It applies only when index rebuild is enabled; in-place reorganization is not a resumable rebuild.

Neither strategy is universally preferable: in-place work favors lower disruption and resource use, while rebuilding can be useful when stronger compaction is worth its additional cost. The observed MedTech space reduction is associated with in-place reorganization; the reported fragmentation reduction of up to 30% is associated with re-indexing. The blocking-protection requirement applies to both modes.

## Options Considered

1. **Keep defrag disabled** - Avoids maintenance-related blocking but forfeits the demonstrated potential for database size and fragmentation reduction.
2. **Enable defrag without blocking safeguards** - Makes the maintenance benefits available but risks prolonged application query blocking.
3. **Enable defrag with detection and cancellation of harmful blocking** - Delivers the maintenance capability while protecting query availability. Chosen.

## Decision

We will enable SQL Server defragmentation through `DefragWatchdog` and make protection against prolonged query blocking a prerequisite for enabling it by default. When SQL Server reports an application read or write request waiting beyond a defined threshold on a lock chain attributable to defrag, the watchdog will cancel the active SQL defrag command through client-side command cancellation. The policy is based on the blocking chain and wait duration, not the SQL statement type; it covers affected `SELECT` and data-modifying requests. Detection must correlate the waiting query, queued statistics operation, and active defrag so unrelated waits are not interrupted. Every watchdog-triggered cancellation must be logged with enough context to identify the affected table, index, partition, blocking condition, and cancellation outcome for subsequent analysis. Cancellation releases the operation's locks and lets queued work proceed. This is the same interruption mechanism for all strategies. If the command is executing a resumable online rebuild, SQL Server retains the resumable operation so a later watchdog run can issue `ALTER INDEX ... RESUME`; an in-place reorganization or non-resumable rebuild is aborted and must be started again.

Both strategies will remain available through the existing setting. In-place reorganization remains the default; rebuilding is an explicit option when its potential compaction benefits justify its higher resource and temporary-space cost. Resumable rebuilds are separately opt-in through `Defrag.IndexRebuild.IsResumable`. On a subsequent watchdog run, `DefragWatchdog` will detect resumable rebuilds left paused after command cancellation and issue `ALTER INDEX ... RESUME` before starting duplicate work. The same blocking detection and stop behavior must apply to either strategy.

The threshold and rollout configuration remain to be established during implementation. The one-second threshold in `DefragBlocking` is a deterministic test trigger, not a production policy. The database size and fragmentation reductions above are observed benefits to use when evaluating the feature; they are not per-database targets or guarantees. Integration coverage should verify watchdog-initiated cancellation of reorganization for a blocked read and write request, pause and later resume of a resumable rebuild, and completion of the queued statistics operation.

## Consequences

- Defragmentation can provide substantial database space savings and reduce fragmentation; the actual benefit depends on database contents and workload.
- In-place reorganization and index rebuild have different tradeoffs in compaction, resource consumption, temporary disk requirements, and operational impact; operators must choose based on workload and available capacity.
- Defrag preserves the existing `AUTO_UPDATE_STATISTICS` and `AUTO_CREATE_STATISTICS` settings (on by default), allowing SQL Server to keep statistics current.
- The watchdog must inspect SQL Server request and lock state and correlate blockers accurately, increasing monitoring complexity and requiring appropriate database visibility. It must also reconcile SQL Server paused-resumable-operation state with defrag queue jobs to resume work without duplicating it.
- Defrag work may be interrupted to protect query availability. A resumable rebuild retains its resumable state when its client command is canceled and can be resumed by a later watchdog run. In-place reorganization is incremental and typically retains completed work when canceled, so a later run generally needs to continue with remaining fragmentation rather than repeat the entire operation. A non-resumable rebuild may need to be retried from the beginning.
- Customers might see short waits on read or write requests while the watchdog detects and resolves blocking. The production blocking threshold and default enablement policy require validation against representative workloads.
- No FHIR resource behavior or API contract change is intended. This extends the existing SQL watchdog approach described in [ADR-2605: Stale Job Queue Monitoring](adr-2605-stale-job-monitor.md).

## References

- `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerWatchdogTests.cs` - `DefragBlocking` and `Defrag`.
