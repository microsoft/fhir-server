# ADR 2610: Least-privilege SQL permissions for the FHIR service managed identity
Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL)

## Context

The SQL persistence provider runs under the FHIR service managed identity. The
identity serves normal FHIR requests and runs background work including
transaction recovery, invisible-history cleanup, event-log cleanup, index
maintenance, geo-replication monitoring, and slow-query diagnostics.

Granting this identity `db_owner` permits unrestricted database control,
including permission administration and arbitrary schema changes. That access
exceeds the FHIR runtime's responsibilities and increases the impact of a
compromised service identity. Schema creation and migration are separate
schema-manager responsibilities. Resource-change partition maintenance is
performed by the Event Agent identity.

The following alternatives were considered:

1. Retain `db_owner`. This preserves all behavior without identifying required
   permissions, but leaves the runtime with unrestricted database control.
2. Replace `db_owner` with `db_ddladmin` and the data roles. This removes
   permission administration but still allows the runtime to create, alter,
   and drop most database objects.
3. Grant only the roles and permissions required by identified runtime
   operations.
4. Replace privileged procedure execution and direct system-view access with
   signed or impersonating modules. This could reduce permissions further, but
   requires coordinated schema changes and changes to direct Query Store
   diagnostics.

## Decision

We will use option 3. The FHIR service managed identity will receive:

| Permission | Runtime use | Rationale |
|---|---|---|
| `db_datareader` | FHIR searches and reads, watchdog configuration reads, queue monitoring, and other direct table reads | The service must read clinical data and runtime-owned operational state. |
| `db_datawriter` | FHIR creates, updates, and deletes; job state; watchdog leases; and operational parameter updates | The service must modify clinical data and runtime-owned operational records. |
| Database `EXECUTE` | FHIR CRUD stored procedures, job operations, cleanup procedures, transaction recovery, index maintenance, and statistics maintenance | The SQL persistence layer performs most work through stored procedures. Procedures that perform DDL, including defragmentation and statistics configuration, use `WITH EXECUTE AS 'dbo'`, so callers do not require direct DDL authority. |
| `VIEW DATABASE STATE` | Geo-replication monitoring through `sys.dm_geo_replication_link_status` and slow-query diagnostics through Query Store views | These system views require database-state visibility. The permission exposes operational metadata but does not permit schema or data modification. |

The FHIR service managed identity will not receive:

| Role or permission | Rationale |
|---|---|
| `db_owner` | No identified FHIR runtime operation requires unrestricted database control, permission administration, or all DDL capabilities. |
| `db_ddladmin` | Runtime DDL is encapsulated in stored procedures that execute as `dbo`. Schema creation and migration belong to schema-manager identities. |
| Database `ALTER` | The FHIR runtime does not directly alter arbitrary database objects. Resource-change partition switching and table truncation belong to the separately provisioned Event Agent identity. |
| `CREATE TABLE` or `ALTER ANY SECURITY POLICY` | These are schema-management capabilities and remain limited to identities that run schema migrations. |

Deployments that host the FHIR service are responsible for provisioning this
permission set and reconciling it for existing databases. New direct SQL
operations must be evaluated against this set. If an operation needs broader
access, a narrowly scoped controlled module is preferred over adding a broad
fixed database role.

## Status

Proposed

## Consequences

### Benefits

- A compromised FHIR runtime identity cannot administer database permissions or
  make arbitrary schema changes through fixed database roles.
- Normal FHIR data access, stored-procedure execution, watchdog maintenance,
  geo-replication monitoring, and slow-query diagnostics remain supported.
- Schema-manager, FHIR runtime, and Event Agent identities retain distinct
  responsibilities and permission boundaries.

### Adverse Effects

- `VIEW DATABASE STATE` exposes database operational metadata, including Query
  Store and replication state, to the FHIR service identity.
- Deployments must reconcile existing database users; changing only new-service
  provisioning leaves stale role memberships in place.
- New runtime SQL paths require permission analysis and may need a schema module
  when the current set is insufficient.

### Neutral Effects

- Schema-manager permissions are unchanged.
- Event Agent permissions for resource-change partition maintenance are
  unchanged.

## References

- [Work item 209571](https://dev.azure.com/microsofthealth/Health/_workitems/edit/209571)
- [sys.dm_geo_replication_link_status permissions](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-geo-replication-link-status-azure-sql-database)

