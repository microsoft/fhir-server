# ADR 2603: Database logging usage
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
In the current FHIR service logging infrastructure timestamps are generated on VMs running the service. Clocks on these VMs are not synchronized precisely. 
Observations in various tests in Azure environment show clock differences between VMs up to 5 seconds. This prevents making precise judgment about order of events in a single FHIR service.
When this precise judgement is preferred, and messages are not frequent, logging to the database can be considered. Examples: search parameter cache sync events.

## Logging infrastructure
dbo.EventLog partitioned table. Assuming short (<1KB) messages, max throughput reaches >8K log messages per second, safe throughput 0.1K messages per second. 
dbo.LogEvent stored procedure.
TryLogEvent C# method in various classes.
CleanupEventLog watchdog is used to periodically trim old records from dbo.EventLog.

## Status
Accepted

## Consequences
### Benefits:
- Improved debuggability

### Adverse Effects:
- If overused can lead to high database usage.

### Neutral Effects:
- No impact.

## References
- https://learn.microsoft.com/en-us/azure/virtual-machines/windows/time-sync

