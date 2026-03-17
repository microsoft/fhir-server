# ADR 2603: Load independent search parameter cache sync
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
There is racing condition between write calls via API and reindex that requires cross VM/POD search param cache syncronization.
If API requests are processed on VM/POD with stale (compared to the VM/POD where reindex orchetrator is running) cache, it will lead to saving resources with incorrect search indexes.
Hence, we need to make sure that all VMs/PODs have the same search param cache before starting reindex processing.
Currently, we just wait for 3 cache refresh periods, hoping that all VMs/PODs will have the same cache. This is not a reliable solution, as VM/POD load can prevent timely cache refreshes.

## Proposed solution
Use database logging https://github.com/microsoft/fhir-server/docs/arch/adr-2602-database-logging.md to track when search param cache is updated and use that information to determine when all VMs/PODs have the same cache before starting reindex processing.

## Details
### General
When search param cache is updated, log that event in the database with a timestamp. There is single method which updates cache GetAndApplySearchParameterUpdates. 
Add logging to that method. Proposed message structure $"Cache in sync={bool} Processed params={count} SearchParamLastUpdated={cache last updated}".
Before starting reindex processing, check the database logs to see when the last cache update occurred and ensure that all VMs/PODs have "cache in sync = true" and "processed params = 0" and identical cache last updated.

### All VMs/PODs definition
There are multiple relatively frequent and very lightweight database calls that each VM/POD is already doing today. For example, each background process attempts to dequeue work from dbo.JobQueue table. 
We can enanble database logging for these calls. Each log record in the dbo.EventLog table has host name column. Distinct list of host names for an interval greater than dequeue period can give us the list of active VMs/PODs. 

## Status
Accepted

## Consequences
### Benefits:
Reliable reindexing

### Adverse Effects:
None.

### Neutral Effects:
Relies on the database functioning normally.

## References
https://github.com/microsoft/fhir-server/docs/arch/adr-2602-database-logging.md

