# Query Store Performance Diagnostics - Baseline Specification

## Status

Agreed baseline for implementation. This document defines the SQL contract, security boundary, operational limits, validation requirements, and repository ownership split. It does not select a Geneva action, PaaS API, or direct-SQL caller.

## Problem

FHIR Azure SQL performance investigations currently require privileged, manual access to Query Store plans, runtime metrics, wait statistics, and statistics metadata. Although Azure SQL can export `QueryStoreWaitStatistics` to Log Analytics, the SQL diagnostics intentionally include plan-level waits so direct SQL and future Geneva callers receive one self-contained slow-query result with runtime metrics, waits, query text, and plan IDs without joining Log Analytics. Support engineers still need a bounded way to:

- identify expensive or regressed query plans;
- retrieve an SSMS-viewable Query Store Showplan;
- compare runtime and wait metrics; and
- inspect statistics freshness, sampling, and cardinality metadata.

The baseline is a self-contained, read-only SQL interface. Filtering, validation, redaction, paging, permissions, and auditing must live in SQL so the procedures can be used by an authorized direct SQL connection or wrapped by future operational tooling.

The canonical database objects belong to the OSS `fhir-server` schema because that repository owns the versioned SQL migration chain consumed by FHIR PaaS. PaaS owns how authorized operators invoke the contract and handle its results.

Related internal guidance:

- `Health.wiki/Home/Olympus-Team/DRI/TSGs/SQL-Latency-Issues.md`
- `Health.wiki/Home/Olympus-Team/Knowledge-Base/Datastore/SQL-DB/SQL-Query-Store---help-with-analyzing-queries.md`
- `Health.wiki/Home/Olympus-Team/DRI/TSGs/SQL-Latency-Issues/SQL-Statistics-Overview.md`
- `Health.wiki/Home/Olympus-Team/Development/SQL-Performance-Automation.md`

## Goals

1. Identify slow or resource-intensive query plans over a bounded time range.
2. Return full Query Store query text to authorized diagnostic callers.
3. Return an SSMS-viewable Query Store Showplan after removing parameter-value metadata.
4. Include Query Store wait statistics in slow-query results when capture is available.
5. Report statistics freshness, sampling, and filter metadata without returning histogram values.
6. Provide an execute-only database role for least-privilege callers.
7. Keep the SQL contract independent of any API, Geneva action, or other caller implementation.

## Non-goals

- Retrieving or capturing an actual execution plan.
- Reconstructing or executing SQL from Query Store.
- Returning statistics histograms, density vectors, or sampled column values.
- Clearing the procedure cache, updating statistics, forcing plans, or changing Query Store configuration.
- Providing caller concurrency control, circuit breaking, command timeouts, artifact retention, or download policy.
- Supporting on-premises SQL Server or self-hosted deployments in the baseline.
- Versioning the result contracts independently of the FHIR database schema.

## Platform and disclosure boundary

### Azure SQL Database

The baseline targets Azure SQL Database and uses only Query Store catalog columns guaranteed across the supported Azure SQL deployment fleet at implementation time. Optional columns that may be rolling out regionally must not be referenced until they are universally available.

### Query Store wait-stat observability

When `sys.database_query_store_options.wait_stats_capture_mode_desc` is `ON`, the slow-query procedure reads `sys.query_store_wait_stats` directly. `WaitStatsStatus` is `Available` for `ON`, `Disabled` for `OFF`, and `Unavailable` for any other value. This lets authorized direct SQL and future Geneva callers retrieve the complete slow-query diagnostic payload—runtime metrics, waits, query text, and plan IDs—from one interface. Azure SQL may also export `QueryStoreWaitStatistics` to Log Analytics, but that stream does not provide the query text or Showplan XML returned by these procedures.

`DatabaseWaitStatistics`, when enabled, remains separate database-level telemetry. It is not plan-level data and is not a substitute for the Query Store wait statistics returned with each slow-query plan.

### Query Store plans are estimated plans

`sys.query_store_plan.query_plan` contains the compile-time Showplan, equivalent to `SET SHOWPLAN_XML ON`. Query Store combines this plan with aggregated runtime statistics; it does not retain an actual plan for every execution.

The baseline reserves the future procedure name:

```text
dbo.GetLastActualQueryPlanDiagnostics
```

This is documentation only. No stub procedure, shared output contract, permission grant, Query Store text execution, `LAST_QUERY_PLAN_STATS` enablement, or plan-cache lookup is included.

## Implementation simplifications

- Plan-type and Parameter Sensitive Plan dispatcher/query-variant metadata are intentionally not read. Those Azure SQL catalog fields are not stable across the supported deployment fleet, so both Query Store procedures omit them rather than returning speculative NULL/status fields or attempting version-specific fallback logic.
- `GetStatisticsHealth` reports database-level `sys.dm_db_stats_properties` metadata for each statistics object. It does not expand incremental statistics into partition-level property rows; unavailable properties remain `NULL` and are explicitly marked `PropertiesUnavailable`. Its table-name input is materialized as `nvarchar(128)`, rather than the type-equivalent `sysname` alias, because the existing schema C# model generator interprets `sysname` as a table-valued parameter.

### Accepted query and plan content

Query text, statement text, scalar expressions, non-parameter constants, object names, missing-index recommendations, warnings, memory grants, and optimizer statistics usage are permitted diagnostic output.

This intentionally accepts that ad hoc or non-parameterized query text and plan constants may contain literal values. The protected content is parameter-value metadata contained in Showplan `ParameterList` elements, including compiled and runtime parameter values.

Statistics histogram values remain excluded because `range_high_key` contains actual indexed-column values.

## Repository ownership

### OSS `fhir-server`

The OSS repository owns the persistent database contract:

- stored procedure definitions;
- `FhirDiagnosticsReader`;
- individual procedure grants;
- schema version and migration scripts;
- SQL aggregation, sanitization, permission, and compatibility tests; and
- the canonical SQL interface documentation.

These objects must be part of the normal `Microsoft.Health.Fhir.SqlServer` schema artifacts and applied by `Microsoft.Health.Fhir.SchemaManager`. A database at the corresponding schema version must not depend on a separate PaaS rollout to acquire them.

Although the supported operational scenario is FHIR PaaS on Azure SQL Database, the OSS migration must remain safe for databases that consume the OSS SQL schema. PaaS-specific identities, storage accounts, APIs, and rollout mechanisms must not be embedded in the OSS procedures.

### `fhir-paas`

The PaaS repository owns the operational integration:

- selecting the initial caller surface, such as direct support tooling, Script Runner, Geneva, or a PaaS administrative operation;
- mapping an approved managed identity or support principal to `FhirDiagnosticsReader`;
- caller authentication and authorization;
- operation-level concurrency control, circuit breaking, and command timeout;
- invoking the OSS stored procedures without caller-supplied SQL;
- formatting, transporting, retaining, and auditing downloaded result artifacts; and
- coordinating deployment after the required OSS package/schema version is available.

PaaS must consume the procedures through the OSS `Microsoft.Health.Fhir.SqlServer` and `Microsoft.Health.Fhir.SchemaManager` packages. It must not maintain a second PaaS-only schema version or duplicate production `CREATE OR ALTER PROCEDURE` definitions.

The PaaS Script Runner may be used for a temporary read-only prototype or to invoke the deployed stored procedures. It must not be the production installation mechanism for these persistent objects. Otherwise a database could report the current OSS schema version while silently lacking the diagnostic procedures or role.

### Rollout dependency

The rollout order is:

1. merge and release the OSS schema change;
2. update `fhir-paas` to consume the OSS package containing that schema version;
3. allow the existing PaaS schema manager flow to apply the migration;
4. provision approved role membership; and
5. enable the PaaS invocation and artifact-handling workflow.

## Stored procedures

All procedures:

- use `WITH EXECUTE AS 'dbo'`;
- use `SET NOCOUNT ON`;
- use no dynamic SQL;
- create no explicit transaction;
- do not change session isolation level, `LOCK_TIMEOUT`, or `XACT_ABORT`;
- return exactly one result set;
- use repository-standard `THROW` errors for invalid calls and unavailable prerequisites; and
- write Start, End, and Error events through `dbo.LogEvent`.

### 1. `dbo.GetQueryStoreSlowQueries`

Returns one row per `query_id + plan_id` for regular executions in Query Store runtime intervals overlapping the requested time range.

#### Inputs

| Parameter | Behavior |
|---|---|
| `@StartTime datetimeoffset = NULL` | Defaults to one hour before the resolved `@EndTime`. |
| `@EndTime datetimeoffset = NULL` | Defaults to `SYSUTCDATETIME()`. |
| `@Top int = 20` | Must be between 1 and 100. |
| `@Offset int = 0` | Must be between 0 and 10,000. `@Offset = 10000` may still return up to 100 rows. |
| `@OrderBy varchar(32) = 'TotalDuration'` | Case-insensitive allowlist described below. |
| `@MinExecutions bigint = 1` | Must be a positive `bigint`. |
| `@QueryTextContains nvarchar(256) = NULL` | Optional literal substring filter. After trimming, it must contain 3-256 characters. |

`@StartTime` and `@EndTime` accept explicit offsets and are normalized to UTC. The start must precede the end, and the requested range must not exceed 24 hours.

`@QueryTextContains`:

- is the only query-content filter;
- is matched under the database collation;
- may contain any caller-supplied text;
- is treated as a literal substring, not a caller-defined `LIKE` pattern;
- escapes `~`, `%`, `_`, and `[` and uses an explicit `ESCAPE N'~'` clause;
- rejects whitespace-only values; and
- is never written to `dbo.LogEvent`.

The `@OrderBy` allowlist is:

- `TotalDuration`
- `AverageDuration`
- `MaximumDuration`
- `TotalCpu`
- `AverageCpu`
- `LogicalReads`
- `Executions`
- `TotalWait`

Unknown order values fail explicitly. Ordering uses a static `CASE` expression rather than dynamic SQL. Diagnostic metrics sort descending, NULL wait totals sort last, and `query_id ASC, plan_id ASC` are deterministic tie-breakers.

There is no execution-type input in the baseline. Runtime and wait metrics include regular executions only. The implementation should contain a focused comment identifying where execution-type support could be added later.

#### Time-window semantics

Query Store runtime rows are interval aggregates. The procedure includes every interval that overlaps the half-open requested range `[StartTime, EndTime)`. Edge intervals may therefore include executions immediately outside the requested timestamps. The result does not repeat the resolved request window or interval boundaries.

#### Runtime aggregation

Query Store can expose multiple in-memory and persisted rows for the active interval. Runtime data must first collapse rows by:

```text
plan_id + execution_type + runtime_stats_interval_id
```

It is then rolled up by `query_id + plan_id`.

Weighted totals and averages use `decimal(38,4)` intermediates:

```text
total duration = SUM(avg_duration * count_executions)
average duration = total duration / SUM(count_executions)
```

The same weighting applies to CPU, reads, writes, and row count. Totals are returned as `decimal(38,0)` and averages as `decimal(38,4)`.

Minimum values use the minimum of interval minima. Maximum values use the maximum of interval maxima. Last values come from the row with the latest execution time, using runtime interval ID and runtime-statistics row ID descending as deterministic tie-breakers.

Query-level compile count and last compile time are repeated on each plan row and must be clearly named as query-level metadata.

Plans with fewer than `@MinExecutions` regular executions in the selected window are excluded. The diagnostic procedures' own Query Store entries are also excluded. All other object-bound and ad hoc Query Store entries are eligible.

#### Wait statistics

Wait statistics are aggregated for the same regular-execution population and overlapping intervals as runtime metrics.

Each result row contains:

- `TotalWaitMilliseconds`
- `AverageWaitMilliseconds`
- `WaitStatsStatus`
- `WaitStatsXml`

`WaitStatsXml` contains one element per wait category, ordered by total wait descending, with:

- category name;
- total wait milliseconds;
- average wait milliseconds; and
- maximum wait milliseconds.

Zero-wait categories are omitted. When wait capture is available but a plan has no waits, the value is an empty typed root such as `<WaitStats />`. When wait capture is disabled or unavailable, `WaitStatsXml` and scalar wait metrics are NULL and `WaitStatsStatus` explains the condition. Other runtime results still return.

If `@OrderBy = 'TotalWait'` while wait capture is disabled or unavailable, rows still return. NULL wait totals sort last.

#### Output

The single result set includes:

- `query_id`
- `plan_id`
- `query_hash`
- `query_plan_hash`
- full `query_sql_text`
- `object_id`
- `object_name`, without a separate schema-name column
- regular execution count
- total, average, minimum, maximum, and last duration in explicitly named microsecond columns
- total and average CPU in explicitly named microsecond columns
- total and average logical reads
- total and average physical reads
- total and average logical writes
- average and maximum row count
- first and last execution time in UTC
- query-level compile count and last compile time
- forced-plan state and available force-failure metadata
- other universally available diagnostic plan metadata; plan-type, dispatcher, and query-variant metadata are omitted
- total and average wait milliseconds
- `WaitStatsStatus`
- `WaitStatsXml`

Physical reads and writes are output metrics but are not ordering options. Query context/handle metadata and execution type are omitted.

Readable Query Store `READ_WRITE` and `READ_ONLY` states return available data. The actual state and read-only reason are logged. `OFF`, `ERROR`, or otherwise unreadable states fail explicitly. A readable store with no qualifying rows returns no rows.

### 2. `dbo.GetQueryStorePlanDiagnostics`

Accepts one required `@PlanId bigint` and returns one row containing Query Store metadata, full query text, and a parameter-redacted Showplan.

An unknown or evicted plan ID fails explicitly with a stable "plan not found or no longer retained" error. The procedure does not fall back to another plan or constrain the plan by a time range.

#### Output

The result includes:

- `PlanId`
- `QueryId`
- `QueryHash`
- `QueryPlanHash`
- full `QuerySqlText`
- engine and compatibility versions
- compile metadata
- trivial, parallel, forced-plan, and force-failure metadata
- first and last execution metadata when available
- `SanitizationStatus`
- `SanitizationErrorCode`
- `SanitizedShowPlanXml`

The entire multi-statement Showplan document is preserved. There is no separate allowlisted `PlanDiagnosticsXml`.
Plan-type, dispatcher, and query-variant metadata are unavailable on the baseline catalog and are omitted.

#### Showplan sanitization

The raw Query Store plan must never be returned. The procedure:

1. copies `query_plan` into a local `xml` variable;
2. counts all elements whose local name is `ParameterList`, regardless of namespace;
3. removes every such element in a single XML DML operation;
4. verifies structurally that no `ParameterList` element and no `ParameterCompiledValue` or `ParameterRuntimeValue` attribute remains;
5. serializes the result and performs a case-insensitive textual check for those forbidden names; and
6. returns the XML only when every verification succeeds.

Unknown Showplan namespaces are processed using the same namespace-agnostic removal and verification. All content other than `ParameterList` elements is preserved, including statement text, non-parameter constants, object/index names, missing-index recommendations, warnings, memory grants, optimizer statistics usage, and plan shape.

There is no serialized plan-size cap.

#### Partial availability

If the plan row exists but `query_plan` is NULL, return the safe metadata with:

- `SanitizedShowPlanXml = NULL`;
- `SanitizationStatus = 'PlanXmlUnavailable'`; and
- a stable non-sensitive error code.

If the XML cannot be parsed or redaction verification fails, return safe metadata only with:

- `SanitizedShowPlanXml = NULL`;
- `SanitizationStatus = 'InvalidXml'` or `'VerificationFailed'`; and
- a stable non-sensitive error code.

Detailed parser messages must not be returned because they may echo plan content. These conditions also write an Error audit event. Raw or partially sanitized XML is never returned as a fallback.

### 3. `dbo.GetStatisticsHealth`

Returns one row per statistics object for user tables.

#### Inputs

| Parameter | Behavior |
|---|---|
| `@TableName nvarchar(128) = NULL` | Optional exact table-name filter under the database collation. |
| `@Top int = 20` | Must be between 1 and 100. |
| `@Offset int = 0` | Must be between 0 and 10,000. |
| `@OrderBy varchar(32) = 'ModificationPercent'` | Case-insensitive allowlist described below. |

There is no statistics-name filter and no minimum modification count/percentage filter.

A supplied table name must be nonblank and resolve to exactly one user table. Unknown names fail explicitly. The baseline assumes FHIR operational tables do not span multiple schemas, so table-name input and output omit schema.

Database-wide results exclude temporal history tables. An exact `@TableName` request may explicitly select a temporal history table.

The `@OrderBy` allowlist is:

- `ModificationCount`
- `ModificationPercent`
- `LastUpdated`
- `SamplingPercent`
- `Rows`

Unknown values fail explicitly. Modification count, modification percentage, sampling percentage, and rows sort descending. `LastUpdated` places NULL values first and then sorts oldest first. Table name and statistics name ascending are deterministic tie-breakers.

#### Sources

- `sys.tables`
- `sys.stats`
- `sys.stats_columns`
- `sys.columns`
- `sys.indexes`
- `sys.dm_db_stats_properties`

The procedure includes index, user-created, and auto-created statistics. Memory-optimized tables are included when compatible metadata is available. Microsoft-shipped and internal tables are excluded.

#### Output

The result includes:

- table name
- statistics name
- statistics ID
- ordered typed XML containing each statistics-column ordinal and name
- auto-created and user-created flags
- incremental, persisted-sample, and no-recompute flags
- filtered-statistics flag and full `filter_definition`
- associated index ID, name, and type description
- disabled and hypothetical index flags
- last update time in UTC
- decimal `HoursSinceLastUpdate`
- row and unfiltered-row counts
- sampled rows
- sampling percentage
- histogram step count, but not histogram contents
- modification counter
- uncapped modification percentage calculated as `modification_counter / rows`
- `StatisticsStatus`

Sampling and modification percentages are NULL when their denominator is zero or required properties are unavailable. Modification percentages may exceed 100 percent.

If `sys.dm_db_stats_properties` returns no row, the statistics object remains in the result with property fields NULL and `StatisticsStatus = 'PropertiesUnavailable'`.

Incremental statistics expose only the incremental flag. Partition-level properties are out of scope.

The procedure must not call `DBCC SHOW_STATISTICS`, `sys.dm_db_stats_histogram`, or any source that returns histogram keys or density vectors.

## Security model

### Database role

Create the database role:

```sql
CREATE ROLE FhirDiagnosticsReader;
```

Grant the role `EXECUTE` individually on:

- `dbo.GetQueryStoreSlowQueries`
- `dbo.GetQueryStorePlanDiagnostics`
- `dbo.GetStatisticsHealth`

Future diagnostic procedures require an explicit reviewed grant. Do not grant schema-level execution on `dbo`.

The role receives no:

- `db_datareader`;
- direct `SELECT` on FHIR tables;
- direct Query Store catalog access;
- `VIEW DATABASE STATE`;
- arbitrary command execution; or
- direct `EXECUTE` permission on `dbo.LogEvent`.

Existing database administrators can use the procedures immediately through their existing privileges. The role is available for future least-privilege callers, with membership controlled independently in each environment.

The "Reader" name describes the externally observable diagnostic behavior. Internal audit writes do not alter Query Store, statistics, plan cache, FHIR data, or schema.

### Caller integration

The initial caller remains undecided. A direct SQL connection, PaaS administrative operation, Geneva action, or other approved tool may invoke the same SQL contract.

Caller authentication, authorization, concurrency control, circuit breaking, command timeout, result retention, and download policy are caller responsibilities. Returned query text and sanitized Showplan are treated as operational metadata, but full artifacts must not be written to general logs, metrics dimensions, or `dbo.LogEvent`.

For the managed PaaS service, these caller responsibilities are implemented in `fhir-paas`; they are not added to the OSS schema migration.

## Resource controls

SQL enforces:

- a default one-hour and hard maximum 24-hour slow-query window;
- `@Top <= 100`;
- `@Offset <= 10000`;
- a positive `@MinExecutions`;
- a 3-256-character literal query-text substring;
- one plan per plan-diagnostics call;
- static SQL only;
- regular-execution-only runtime and wait aggregation; and
- exclusion of the diagnostic procedures' own Query Store entries.

Limits are hard-coded in the procedures. There is no `dbo.Parameters` kill switch, SQL concurrency gate, plan-size cap, total-count query, continuation token, or `HasMoreRows` result.

## Audit and observability

Every procedure follows the existing `dbo.LogEvent` pattern and writes Start, End, and Error events, including successful calls.

Audit records include:

- `ORIGINAL_LOGIN()`;
- effective database principal from `USER_NAME()`;
- procedure name;
- bounded request metadata;
- elapsed milliseconds;
- returned row count; and
- sanitized XML size for successful plan retrieval.

Slow-query audit metadata includes resolved UTC window, ordering mode, `@Top`, `@Offset`, `@MinExecutions`, Query Store state/read-only reason, wait-statistics capture mode, and query-text filter presence/length, but never the filter text or wait payload.

Plan audit metadata includes `plan_id`, sanitization status, stable error code, and result size, but never query text or XML.

Statistics audit metadata includes the exact validated table name when supplied, ordering mode, `@Top`, and `@Offset`.

Audit records must not contain:

- query text;
- `@QueryTextContains`;
- Showplan XML;
- parameter values;
- histogram values; or
- every query/plan ID returned by a page.

If Start, End, or Error logging fails, the diagnostic call fails. Callers do not receive direct permission to invoke `dbo.LogEvent`.

## Testing requirements

**Deferred prototype validation:** The prototype has one representative SQL-backed end-to-end path. Exhaustive matrix validation remains future work, including negative validation, fail-closed audit behavior, permissions, a malformed-fixture corpus, and wait-disabled cases.

### Slow-query aggregation

1. Duplicate active-interval in-memory/persisted rows are collapsed before rollup.
2. Weighted totals and averages use the agreed decimal precision.
3. Minimum, maximum, and deterministic last-value calculations are correct.
4. Only regular executions contribute to runtime and wait metrics.
5. Overlapping Query Store interval semantics are verified at both window boundaries.
6. Time, row, offset, minimum-execution, query-text, and order allowlists cannot be bypassed.
7. Literal query-text matching correctly escapes `~`, `%`, `_`, and `[`.
8. Query Store `READ_WRITE` and readable `READ_ONLY` states return data.
9. Query Store `OFF`, `ERROR`, and unreadable states fail with actionable errors.
10. Wait capture available, disabled, unavailable, empty, and `TotalWait` ordering cases are covered.
11. Diagnostic procedures exclude their own Query Store entries.

### Showplan sanitization

1. Fixtures include single- and multi-statement plans.
2. Fixtures include compiled values, runtime values, multiple `ParameterList` elements, plans without parameters, unusual namespaces/extensions, PSP/variant plans when available, large/deep plans, and malformed XML.
3. Fixtures contain PHI-shaped parameter values.
4. Serialized output contains no `ParameterList`, `ParameterCompiledValue`, or `ParameterRuntimeValue`.
5. Statement text, non-parameter constants, missing-index recommendations, warnings, and other non-parameter content remain unchanged.
6. Unknown namespaces sanitize successfully when verification passes.
7. NULL, malformed, and verification-failing XML returns metadata only with the correct stable status/code.
8. Raw or partially sanitized XML is never returned.
9. Representative sanitized plans are manually verified to open in the SSMS graphical plan viewer.

### Statistics

1. All statistics types are returned with correct ordered column XML.
2. Filter definitions, index metadata, disabled/hypothetical flags, and missing-property status are correct.
3. Sampling/modification percentage zero-denominator behavior is correct.
4. Modification percentages above 100 percent are preserved.
5. Database-wide temporal-history exclusion and explicit history-table inclusion are covered.
6. Histogram keys and density vectors never appear.

### Permissions and integration

1. A principal with `FhirDiagnosticsReader` can execute all three procedures.
2. Procedures use `EXECUTE AS 'dbo'` and capture both original and effective identities.
3. Start, End, and Error audit behavior is verified, including fail-closed logging failures.
4. Procedures remain read-only except for required audit events.
5. Deterministic fixture tests are supplemented by Azure SQL integration tests for live catalog compatibility.
6. OSS tests verify the persistent SQL contract without depending on PaaS assemblies or infrastructure.
7. PaaS tests verify package/schema-version synchronization, role provisioning, stored-procedure invocation, and artifact handling without duplicating the SQL implementation.

## Schema and rollout

### OSS schema change

- Add all three procedures under `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs`.
- Add an idempotent role and individual permission migration.
- Introduce all three procedures and the role in one database schema version and migration diff.
- Include the objects in the generated full schema and packaged SchemaManager resources.
- Keep the migration additive and compatible with the previous application release.
- Use normal repository code review and automated/manual validation. No separate security-review gate is required.
- Keep actual-plan diagnostics as a documented future feature only.

### PaaS integration change

- Update the OSS FHIR package versions and synchronized target schema version through the existing `fhir-paas` dependency flow.
- Do not copy the stored procedure or role DDL into PaaS Script Runner scripts.
- Add role membership only for the approved operational identity.
- Implement the selected caller, result transport, artifact storage, and operational authorization in `fhir-paas`.
- Deploy schema/package consumption before enabling the caller.
- Control caller rollout and role membership independently in each environment.

## References

- [Monitor performance by using Query Store](https://learn.microsoft.com/sql/relational-databases/performance/monitoring-performance-by-using-the-query-store)
- [How Query Store collects data](https://learn.microsoft.com/sql/relational-databases/performance/how-query-store-collects-data)
- [`sys.query_store_runtime_stats`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql)
- [`sys.query_store_wait_stats`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-wait-stats-transact-sql)
- [`sys.query_store_plan`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-plan-transact-sql)
- [`sys.query_store_query`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-query-transact-sql)
- [`sys.query_store_query_text`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-query-text-transact-sql)
- [`sys.database_query_store_options`](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-database-query-store-options-transact-sql)
- [`sys.dm_exec_query_plan_stats`](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-plan-stats-transact-sql)
- [`sys.dm_db_stats_properties`](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-db-stats-properties-transact-sql)
- [`sys.dm_db_stats_histogram`](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-db-stats-histogram-transact-sql)
- [Showplan XML schemas](https://schemas.microsoft.com/sqlserver/2004/07/showplan/)
