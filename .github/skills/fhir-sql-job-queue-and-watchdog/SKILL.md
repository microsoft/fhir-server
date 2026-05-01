---
name: fhir-sql-job-queue-and-watchdog
description: |
  JobQueue and Watchdog patterns in the Microsoft FHIR Server SQL schema.
  Activate when: working with background jobs, the job queue, export/import/reindex operations,
  "JobQueue", "DequeueJob", "EnqueueJobs", "PutJobHeartbeat", "WatchdogLeases",
  "AcquireWatchdogLease", queue partitioning, "QueueType", "GroupId", job lifecycle,
  heartbeat, cancel, archive, 16-partition, "TinyintPartitionScheme", watchdog coordination,
  distributed locking, lease management, background maintenance.
---

# FHIR SQL Job Queue and Watchdog

## When to use this skill

Use this skill whenever you need to:
- Add a new background job type to the FHIR server
- Modify the dequeue/heartbeat/cancel lifecycle
- Understand the 16-partition hash design
- Add or modify watchdog-coordinated maintenance tasks
- Debug job processing issues

## Core invariants

1. **JobQueue uses 16-way hash partitioning**: `PartitionId = JobId % 16` on `TinyintPartitionScheme/TinyintPartitionFunction`. This distributes lock contention across 16 independent partitions. Never change the partition count without understanding the full implications.

2. **DequeueJob uses sp_getapplock per QueueType**: One worker at a time can dequeue from a given queue type within a partition. The lock is held for the transaction duration.

3. **Job state machine**: Created (0) → Running (1) → Completed (2) / Failed (3) / Cancelled (4). Transitions are unidirectional — a Completed job cannot return to Running.

4. **Optimistic concurrency via Version column**: Every heartbeat increments `Version`. Status updates and heartbeats include the expected Version — rejected if it has been incremented by another worker (stale job takeover detection).

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

### Dequeue pattern
```sql
-- Acquire exclusive lock on queue type
EXECUTE @LockResult = sp_getapplock @Resource = 'DequeueJob_QueueType_'+convert(varchar,@QueueType),
                                    @LockMode = 'Exclusive', @LockOwner = 'Transaction'

-- Find next available job (Status = 0 Created, ordered by Priority then CreateDate)
UPDATE TOP (1) dbo.JobQueue
SET Status = 1,  -- Running
    StartDate = getUTCdate(),
    HeartbeatDate = getUTCdate(),
    Worker = @Worker,
    Version = Version + 1
OUTPUT inserted.*
WHERE QueueType = @QueueType
  AND Status = 0
  AND PartitionId = @PartitionId  -- Hash partition targeting
ORDER BY Priority, CreateDate
```

### Heartbeat pattern
```sql
UPDATE dbo.JobQueue
SET HeartbeatDate = getUTCdate(),
    Version = Version + 1,
    Data = @Data  -- Progress checkpoint
WHERE QueueType = @QueueType
  AND JobId = @JobId
  AND Version = @ExpectedVersion  -- Optimistic concurrency
  AND Status = 1  -- Must be Running
```

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

- **Ignoring partition targeting**: Always include `PartitionId` in JobQueue queries for partition elimination
- **Not checking Version on heartbeat**: Without version check, a job that was reassigned could have two workers processing it simultaneously
- **Blocking cancellation**: Workers must periodically check `CancelRequested` — long-running operations should check at reasonable intervals
- **Holding sp_getapplock too long**: The exclusive lock blocks all other dequeue attempts for that queue type — keep the critical section minimal
- **Creating a new QueueType without updating the Watchdog**: New queue types need corresponding watchdog coordination for stale job recovery
- **Forgetting HeartbeatDate update**: Jobs with stale heartbeats are treated as abandoned and reassigned

## Checklist before committing

- [ ] New job types use the existing JobQueue table (don't create new queue tables)
- [ ] QueueType constant defined in C# code
- [ ] Partition hash (JobId % 16) is preserved in all queries
- [ ] sp_getapplock used for dequeue serialization
- [ ] Version-based optimistic concurrency on all state transitions
- [ ] Heartbeat interval configured appropriately for job duration
- [ ] CancelRequested checked in worker processing loop
- [ ] Watchdog configured to monitor new queue type for stale jobs
- [ ] LogEvent calls added at job state transitions

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/JobQueue.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/EnqueueJobs.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/DequeueJob.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobHeartbeat.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobStatus.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/PutJobCancelation.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/WatchdogLeases.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/AcquireWatchdogLease.sql`
