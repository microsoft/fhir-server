---
name: fhir-sql-background-jobs
description: |
  JobQueue and Watchdog patterns in the Microsoft FHIR Server SQL schema.
  Activate when: working with background jobs, the job queue, export/import/reindex operations,
  "JobQueue", "DequeueJob", "EnqueueJobs", "PutJobHeartbeat", "WatchdogLeases",
  "AcquireWatchdogLease", queue partitioning, "QueueType", "GroupId", job lifecycle,
  heartbeat, cancel, archive, "PartitionId", "JobId % 16", "TinyintPartitionScheme", watchdog coordination,
  distributed locking, lease management, background maintenance.
---

# FHIR SQL Job Queue and Watchdog

## When to use this skill

Use this skill whenever you need to:
- Add a new background job type to the FHIR server
- Modify the dequeue/heartbeat/cancel lifecycle
- Understand the per-QueueType partitioning + `JobId % 16` contention-spreading design
- Add or modify watchdog-coordinated maintenance tasks
- Debug job processing issues

## Core invariants

1. **JobQueue is physically partitioned by `QueueType`** on `TinyintPartitionScheme` (backed by `TinyintPartitionFunction`, which has 256 RANGE RIGHT boundaries 0..255). Each `QueueType` (1=export, 2=import, 3=...) lands in its own partition, isolating I/O and locking between queue types.

2. **`PartitionId = convert(tinyint, JobId % 16) PERSISTED` is a computed column, not a partition key.** It is used to physically separate dequeue contention within a queue type via the nonclustered index `IX_QueueType_PartitionId_Status_Priority (PartitionId, Status, Priority)`. The clustered key is `(QueueType, PartitionId, JobId)`. The constant `16` is hardcoded in `DequeueJob` as `@MaxPartitions tinyint = 16`.

3. **DequeueJob uses `sp_getapplock` per `(Status, QueueType, PartitionId)` triple.** The lock resource string is `'DequeueJob_'+Status+'_'+QueueType+'_'+PartitionId` (Status 0 = ready jobs, Status 1 = timed-out jobs). `DequeueJob` round-robins through PartitionIds 0..15 looking for work, taking the lock briefly per partition. The lock is held for the transaction duration (`@LockOwner = 'Transaction'` is the default).

3. **Job state machine**: Created (0) → Running (1) → Completed (2) / Failed (3) / Cancelled (4) / Archived (5). Transitions are unidirectional — a Completed job cannot return to Running.

4. **Optimistic concurrency via `Version` column**: `Version` defaults to `datediff_big(millisecond,'0001-01-01',getUTCdate())` and is reset on every state-changing UPDATE. `PutJobHeartbeat`, `PutJobStatus`, and `PutJobCancelation` accept the caller's expected `Version` and reject if it has been bumped (stale-worker takeover detection).

5. **Heartbeat-based liveness**: `HeartbeatDate` is checked by the Watchdog. Jobs with expired heartbeats (beyond a configurable timeout) are eligible for reassignment to another worker.

6. **WatchdogLeases ensure single-instance execution**: Only one FHIR server instance holds the lease for a given Watchdog. The lease has an expiration time; if the holder fails to renew, another instance can acquire it.

7. **CancelRequested is advisory**: Setting `CancelRequested = 1` does not immediately stop the job — the worker must check this flag during processing and exit gracefully.

## Required patterns

### Enqueue jobs
```sql
-- EnqueueJobs takes a list of job definitions and creates them in the queue
INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Version, Status, Priority, CreateDate, HeartbeatDate)
SELECT @QueueType, @GroupId, @NextJobId + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1,
       Definition, DefinitionHash, 0, 0, @Priority, getUTCdate(), getUTCdate()
FROM @Definitions
```

### Dequeue pattern (round-robin over PartitionId 0..15)
```sql
DECLARE @MaxPartitions tinyint = 16  -- hardcoded in DequeueJob
DECLARE @PartitionId tinyint = @MaxPartitions * rand()  -- random start, or @InputJobId % 16
DECLARE @Lock varchar(100)

WHILE @JobId IS NULL AND @LookedAtPartitions < @MaxPartitions
BEGIN
  SET @Lock = 'DequeueJob_0_'+convert(varchar,@QueueType)+'_'+convert(varchar,@PartitionId)
  BEGIN TRANSACTION
  EXECUTE sp_getapplock @Lock, 'Exclusive'

  UPDATE T
    SET StartDate = getUTCdate()
       ,HeartbeatDate = getUTCdate()
       ,Worker = @Worker
       ,Status = 1
       ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
       ,@JobId = T.JobId
    FROM dbo.JobQueue T WITH (PAGLOCK)
         JOIN (SELECT TOP 1 JobId
                 FROM dbo.JobQueue WITH (INDEX = IX_QueueType_PartitionId_Status_Priority)
                 WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND Status = 0
                 ORDER BY Priority, JobId) S
           ON QueueType = @QueueType AND PartitionId = @PartitionId AND T.JobId = S.JobId
  COMMIT TRANSACTION

  IF @JobId IS NULL SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
END
```
Note: `Version` is *replaced* with the current millisecond timestamp (not incremented). A second WHILE loop with `@Lock = 'DequeueJob_1_...'` and `Status = 1 AND datediff(second,HeartbeatDate,...) > @HeartbeatTimeoutSec` reclaims timed-out jobs. Pass `@CheckTimeoutJobs = 1` to skip the Status=0 first pass entirely (used by Watchdog-only callers).

> ⚠️ **Cross-pass race**: The Status=0 lock (`DequeueJob_0_<QueueType>_<PartitionId>`) and the Status=1 lock (`DequeueJob_1_<QueueType>_<PartitionId>`) are **different** applock resource strings. Two workers on different passes can hold their respective locks simultaneously and contend on the same partition's pages via `WITH (PAGLOCK)`. Under 8+ workers with no backoff, this is the primary deadlock cycle path. Worker-side fix: add backoff when `DequeueAsync` returns null; optionally assign each worker a dedicated starting partition to reduce overlap.

### Heartbeat pattern
```sql
UPDATE dbo.JobQueue
SET HeartbeatDate = getUTCdate(),
    Version = datediff_big(millisecond,'0001-01-01',getUTCdate()),  -- REPLACE, do not increment
    Data = @Data  -- Progress checkpoint
WHERE QueueType = @QueueType
  AND JobId = @JobId
  AND Version = @ExpectedVersion  -- Optimistic concurrency check
  AND Status = 1  -- Must be Running
```
> ⚠️ `Version` is always **replaced** with the current millisecond timestamp — never `Version + 1`. This is consistent with `DequeueJob` (line 49) and `PutJobHeartbeat`. Incrementing would break lease takeover detection because Watchdog compares absolute timestamps, not relative counters.

### Watchdog lease acquisition
```sql
-- Try to acquire lease (insert or update)
UPDATE dbo.WatchdogLeases
SET LeaseHolder = @InstanceId,
    LeaseEndTime = DATEADD(second, @LeaseDurationSeconds, getUTCdate()),
    LeaseRequestor = NULL
WHERE Watchdog = @WatchdogName
  AND (LeaseEndTime < getUTCdate()  -- Expired
       OR LeaseHolder = @InstanceId)  -- Already ours
```

## Common mistakes to avoid

- **Confusing `PartitionId` with the SQL partition key**: The table is partitioned by `QueueType`, not `PartitionId`. `PartitionId` is a `JobId % 16` computed column used as the leading key of the dequeue nonclustered index for contention spreading.
- **Using a single dequeue lock per QueueType**: That would serialize all workers. The actual scheme uses a lock per `(Status, QueueType, PartitionId)` so up to 16 workers can dequeue concurrently within a queue type.
- **Incrementing `Version` instead of replacing it**: The codebase sets `Version = datediff_big(millisecond,'0001-01-01',getUTCdate())`. Optimistic concurrency relies on monotonic timestamps, not increments. Both `DequeueJob` and `PutJobHeartbeat` use timestamp replacement.
- **No worker backoff when queue is empty**: Calling `DequeueAsync` in a tight loop when no jobs are available executes up to 32 transactions per call (16 partitions × 2 passes). With multiple workers this causes PAGLOCK churn and deadlock cycles. Always add jittered backoff (e.g. 1-2s) on null returns.
- **All workers sweeping all partitions concurrently**: The 16-partition contention-spreading design only helps if workers stay in different lanes. Consider assigning each worker a preferred starting partition (`startPartition = workerId * (16 / workerCount)`) to reduce cross-worker lock overlap.
- **Blocking cancellation**: Workers must periodically check `CancelRequested` — long-running operations should check at reasonable intervals.
- **Holding sp_getapplock too long**: The exclusive lock blocks all other dequeue attempts for that `(QueueType, PartitionId)` slot — keep the critical section minimal.
- **Creating a new QueueType without updating the Watchdog**: New queue types need corresponding watchdog coordination for stale job recovery.
- **Forgetting HeartbeatDate update**: Jobs with stale heartbeats are picked up by the second `DequeueJob` pass (Status=1 with timeout) and reassigned.

## Checklist before committing

- [ ] New job types use the existing JobQueue table (don't create new queue tables)
- [ ] QueueType constant defined in C# code
- [ ] `PartitionId = JobId % 16` predicate (or equivalent) is included when querying `JobQueue` so the dequeue index `IX_QueueType_PartitionId_Status_Priority` can seek
- [ ] sp_getapplock used for dequeue serialization
- [ ] Version-based optimistic concurrency on all state transitions
- [ ] Heartbeat interval configured appropriately for job duration
- [ ] CancelRequested checked in worker processing loop
- [ ] Watchdog configured to monitor new queue type for stale jobs
- [ ] LogEvent calls added at job state transitions
- [ ] Worker loop includes backoff delay on null dequeue result (avoid PAGLOCK churn)
- [ ] `@CheckTimeoutJobs = 1` parameter used only from Watchdog context (skips Status=0 pass)

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/JobQueue.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/EnqueueJobs.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/DequeueJob.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobHeartbeat.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobStatus.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobCancelation.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/WatchdogLeases.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/AcquireWatchdogLease.sql`
