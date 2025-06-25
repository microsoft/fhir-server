# ADR 2311: Limit Concurrent Calls to MergeResources Stored Procedure
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
The MergeResources stored procedure is critical for writing FHIR resources to store efficiently. However, excessive concurrent calls to this procedure can degrade database performance and significantly increase write latencies. To mitigate this, we need a mechanism to limit concurrent executions of MergeResources.

## Decision
We will implement infrastructure to set the application name in the connection string specifically to "MergeResources". Additionally, we will modify the MergeResourceBeginTransaction stored procedure to include a concurrency check using the following logic:

```sql
SELECT count(*) FROM sys.dm_exec_sessions WHERE status <> 'sleeping' AND program_name = 'MergeResources'
```

If the count exceeds the configured threshold defined by the parameter `MergeResources.OptimalConcurrentCalls`, the stored procedure will raise an exception. This exception will be caught by the FHIR server code, triggering a retry mechanism with exponential backoff.

Initially, this change will only introduce the infrastructure and exception handling. A subsequent work item will implement logic to return HTTP 429 (Too Many Requests) responses to API callers when concurrency limits are exceeded.

## Status
Accepted

## Consequences
### Benefits:
- Improved write througput
- Reduced latency for requests exceeding concurrency limits.
- Enhanced scalability and stability of the MergeResources operation.

### Adverse Effects:
- Additional complexity in handling exceptions and retry logic.

### Neutral Effects:
- No immediate impact on API consumers until subsequent work item implements HTTP 429 responses.

## References
- https://github.com/microsoft/fhir-server/pull/4863
- https://github.com/microsoft/fhir-server/pull/4907
- https://github.com/microsoft/fhir-server/pull/4926
