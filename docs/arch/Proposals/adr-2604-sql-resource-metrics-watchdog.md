# ADR 2604: SQL resource metrics watchdog
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context
The server already emits request-oriented metrics through the shared `FhirServer` meter, and the SQL provider already runs periodic background work through the watchdog infrastructure. What is missing is a lightweight way to emit periodic SQL resource-health signals from inside the service so request behavior can be correlated with database saturation.

For Azure SQL Database, `sys.dm_db_resource_stats` is the lowest-friction source for this data. It is available through the existing SQL connection path, updates every 15 seconds, and exposes the resource fields we care about for v1: CPU, data I/O, log write I/O, memory, workers, and sessions. It does require `VIEW DATABASE STATE`, and it is not the correct source for Azure SQL Managed Instance.

## Decision
We will add a SQL metrics watchdog for the SQL provider that runs once per minute and emits Azure SQL Database resource metrics through the existing meter pattern.

The design will:
- Extend `FhirServer:Watchdog` with `SqlMetrics` configuration containing `Enabled` and `PeriodSeconds`.
- Add a SQL reader abstraction that fetches the latest row from `sys.dm_db_resource_stats`.
- Add a SQL metric handler that records the sampled values under the `FhirServer` meter using dotted names:
  - `Sql.Database.CpuPercent`
  - `Sql.Database.DataIoPercent`
  - `Sql.Database.LogIoPercent`
  - `Sql.Database.MemoryPercent`
  - `Sql.Database.WorkersPercent`
  - `Sql.Database.SessionsPercent`
- Use histogram instruments for v1 so the feature fits the existing push-style metric handler model without introducing observable-state callbacks.
- Integrate the watchdog into the SQL watchdog orchestration and make it fault tolerant: startup must not fail, failures must be logged, and the next interval must continue normally.

The first version is scoped to Azure SQL Database only. Optional instance-level metrics (`Sql.Database.InstanceCpuPercent` and `Sql.Database.InstanceMemoryPercent`) can be added in a later phase once the base path is reviewed and accepted.

## Status
Proposed

## Consequences
### Benefits:
- Gives operators direct visibility into SQL resource pressure from the service itself without adding Azure Monitor or ARM dependencies.
- Reuses existing watchdog, configuration, DI, and meter conventions, which keeps the implementation small and consistent with current SQL-side architecture.
- Improves correlation between application latency/failures and underlying database saturation.

### Adverse Effects:
- The feature depends on `VIEW DATABASE STATE`; deployments without that permission will not emit metrics.
- Metrics are periodic samples rather than continuously observed gauges, so consumers must interpret them as sampled telemetry.
- The initial design does not cover Azure SQL Managed Instance.

### Neutral Effects:
- No alerting, fleet aggregation, or query-level diagnostics are included in this decision.
- No new external service dependency is introduced for v1.

## References
- `docs\arch\adr-2603-expired-resource-cleanup.md`
- `src\Microsoft.Health.Fhir.Core\Logging\Metrics\BaseMeterMetricHandler.cs`
- `src\Microsoft.Health.Fhir.SqlServer\Features\Watchdogs\Watchdog.cs`
- `src\Microsoft.Health.Fhir.SqlServer\Features\Watchdogs\FhirTimer.cs`
