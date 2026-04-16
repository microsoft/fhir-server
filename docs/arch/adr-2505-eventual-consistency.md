# ADR 2505: Enable eventual consistency for maintaining search param indexes in sync with the Resource table
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
Efficiency of writes to FHIR SQL storage is higly dependent on duration of SQL transactions. Long SQL transactions cause significant blocking between parallel ingestion processes and reduce overall write throughput. This is usually observered for large hyperscale databases (>1TB). Currently FHIR SQL supports strong consistency when updates in the Resource table and the search param tables are included in the same SQL transaction. This makes SQL transactions long.

## Decision
We will implement eventual consistency for maintaining search param indexes. Updates of resources will still run in single SQL transaction as today (strong consistency), because they involve deletion of previous search param rows. For resource creates only, we will run all writes in separate SQL transactions, starting with insert into the Resource table. In case of success this is equivalent to current strong consistency behavior. In case of interruptions search param indexes might not be completely in sync with corresponding row in the Resource table. This will be addressed by the Transaction watchdog, which will roll transaction forward based on data committed in the Resource table.

Initially, this change will be enabled for $import only. We will introduce new flag "EventualConsistency" which will accept true/false values with default value false (strong consistency, current behavior).

## Status
Accepted

## Consequences
### Benefits:
- Improved write througput for large hyperscale databases (>1TB)

### Neutral Effects:
- No impact on current customer logic.
