# ADR: Introduce `$stats` FHIR Operation

*Labels*: [API](https://github.com/microsoft/fhir-server/labels/Area-API) | [SQL Server](https://github.com/microsoft/fhir-server/labels/Area-SqlServer)

---

## Scope

This operation is only applicable to SQL Server data stores in Microsoft FHIR Server.

## Context

Healthcare organizations operating FHIR servers need visibility into their data footprint for capacity planning, monitoring, and operational reporting. Currently, there is no efficient way to obtain aggregate resource counts without executing expensive search queries across all resource types. This creates challenges for:

- **Capacity Planning**: Understanding the distribution of resources across types
- **Monitoring**: Tracking growth patterns and identifying unusual changes
- **Operational Reporting**: Providing administrators with database health metrics
- **Cost Management**: Understanding storage utilization by resource type

### Use Cases

| # | Scenario |
|---|----------|
| 1 | Administrator needs to understand the distribution of resources across all types |
| 2 | Operations team monitors growth trends by comparing stats over time |
| 3 | Capacity planning requires understanding total vs active resource counts |
| 4 | Support team needs quick visibility into database state for troubleshooting |
| 5 | Automated monitoring systems track resource counts for alerting |

### Problem Statement

Traditional approaches to gathering resource statistics have significant drawbacks:

1. **Full Table Scans**: Counting resources via search queries requires scanning the Resource table
2. **Performance Impact**: Large databases can experience degraded performance during counting operations
3. **No Active/History Distinction**: Standard FHIR search doesn't easily distinguish between active resources and historical versions
4. **Resource Intensive**: Aggregating counts across all resource types requires multiple queries

## Decision

We will implement a new `$stats` operation at the system level that provides resource statistics using SQL Server's partition metadata and index statistics. This approach leverages the existing table partitioning strategy (by `ResourceTypeId`) to provide near-instantaneous counts without scanning the actual data.

### Implementation Details

#### Endpoint

```http
GET [base]/$stats
```

#### Response Format

The operation returns a FHIR `Parameters` resource containing statistics for each resource type:

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "resourceType",
      "part": [
        { "name": "name", "valueString": "Patient" },
        { "name": "totalCount", "valueInteger64": 1500000 },
        { "name": "activeCount", "valueInteger64": 1250000 }
      ]
    },
    {
      "name": "resourceType",
      "part": [
        { "name": "name", "valueString": "Observation" },
        { "name": "totalCount", "valueInteger64": 25000000 },
        { "name": "activeCount", "valueInteger64": 20000000 }
      ]
    }
  ]
}
```

#### Statistics Definitions

| Statistic | Description |
|-----------|-------------|
| `name` | The FHIR resource type name (e.g., "Patient", "Observation") |
| `totalCount` | Total number of resource records including historical versions and soft-deleted resources |
| `activeCount` | Number of current (non-historical, non-deleted) resources - represents the "live" data |

#### SQL Server Implementation

The implementation uses a stored procedure (`dbo.GetResourceStats`) that queries SQL Server's `sys.dm_db_partition_stats` system view to retrieve row counts from partition metadata. This approach:

1. **Leverages Partition Scheme**: Uses `PartitionScheme_ResourceTypeId` which partitions the Resource table by resource type
2. **Uses Clustered Index Stats**: Retrieves total counts from the clustered index (index_id = 1)
3. **Uses Secondary Index Stats**: Retrieves active counts from the `IX_Resource_ResourceTypeId_ResourceSurrgateId` filtered index which only contains non-historical, non-deleted resources
4. **Avoids Table Scans**: Reads metadata rather than actual data rows

```sql
CREATE PROCEDURE dbo.GetResourceStats
AS
  SELECT ResourceType = (SELECT Name FROM ResourceType WHERE ResourceTypeId = ...)
        ,A.Rows as TotalRows
        ,B.Rows as ActiveRows
  FROM (... clustered index partition stats ...) A
  JOIN (... filtered index partition stats ...) B 
    ON A.partition_number = B.partition_number
  WHERE A.Rows > 0
  ORDER BY ResourceType
```

#### Architecture Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `StatsRequest` | `Microsoft.Health.Fhir.Core/Messages/Stats/` | MediatR request message |
| `StatsResponse` | `Microsoft.Health.Fhir.Core/Messages/Stats/` | Response containing statistics dictionary |
| `ResourceTypeStats` | `Microsoft.Health.Fhir.Core/Messages/Stats/` | Statistics for a single resource type |
| `StatsHandler` | `Microsoft.Health.Fhir.SqlServer/Features/Stats/` | MediatR handler for SQL Server |
| `SqlServerStatsProvider` | `Microsoft.Health.Fhir.SqlServer/Features/Stats/` | Data access layer for statistics |
| `StatsResponseExtensions` | `Microsoft.Health.Fhir.Shared.Api/Features/Resources/` | Converts response to FHIR Parameters |
| `GetResourceStats.sql` | `Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/` | Stored procedure definition |

#### Schema Migration

This feature requires schema version 103, which adds the `GetResourceStats` stored procedure.

### Why Not Use Standard FHIR Search?

| Approach | Pros | Cons |
|----------|------|------|
| `GET /Patient?_summary=count` | Standard FHIR | Requires one call per resource type; scans data |
| `$stats` operation | Single call; uses metadata; O(1) performance | Non-standard operation |

The `$stats` operation was chosen because:
- **Performance**: O(1) complexity regardless of database size
- **Comprehensiveness**: Returns all resource types in a single call
- **Active/Total Split**: Provides distinction between total and active counts
- **No Data Scanning**: Uses partition metadata rather than actual data

### Security Considerations

- The `$stats` endpoint follows standard FHIR authorization
- Returns aggregate counts only - no PHI is exposed
- Audit logging is enabled via `AuditEventType.Stats`

## Status

Accepted

## Consequences

### Benefits

| Benefit | Description |
|---------|-------------|
| Fast Performance | O(1) performance regardless of database size |
| Single Request | All resource type statistics in one API call |
| Operational Visibility | Clear distinction between total and active resources |
| Low Resource Usage | No table scans or data reads required |
| Standard Response | Returns valid FHIR Parameters resource |

### Trade-offs

| Trade-off | Mitigation |
|-----------|------------|
| SQL Server Only | Cosmos DB implementation can be added later if needed |
| Non-standard Operation | Uses FHIR Parameters resource for response; follows common patterns |
| Approximate Counts | Partition stats may have slight delays; acceptable for monitoring use cases |
| No Filtering | Partition stats don't allow for filtering; required for acceptable performance on large databases |

### Future Enhancements

**Historical Trending**: Store snapshots for trend analysis

## References

- [FHIR Parameters Resource](https://hl7.org/fhir/parameters.html)
- [SQL Server sys.dm_db_partition_stats](https://docs.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-partition-stats-transact-sql)
- [FHIR Server SQL Partitioning](../SchemaMigrationGuide.md)
