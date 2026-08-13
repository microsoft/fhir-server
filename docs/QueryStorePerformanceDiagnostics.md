# Query Store Performance Diagnostics - Baseline Specification

## Status

Agreed baseline for implementation. This document defines the SQL contract, security boundary, operational limits, and validation requirements. It does not select a Geneva action, PaaS API, or direct-SQL caller.

## Problem

FHIR Azure SQL performance investigations currently require privileged, manual access to Query Store plans, runtime metrics, wait statistics, and statistics metadata. Existing Log Analytics data provides some Query Store information, but support engineers still need a bounded way to:

- identify expensive or regressed query plans;
- retrieve an SSMS-viewable Query Store Showplan;
- compare runtime and wait metrics; and
- inspect statistics freshness, sampling, and cardinality metadata.

The baseline is a self-contained, read-only SQL interface. Filtering, validation, redaction, paging, permissions, and auditing must live in SQL so the procedures can be used by an authorized direct SQL connection or wrapped by future operational tooling.

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

### Query Store plans are estimated plans

`sys.query_store_plan.query_plan` contains the compile-time Showplan, equivalent to `SET SHOWPLAN_XML ON`. Query Store combines this plan with aggregated runtime statistics; it does not retain an actual plan for every execution.

The baseline reserves the future procedure name:

```text
dbo.GetLastActualQueryPlanDiagnostics
```

This is documentation only. No stub procedure, shared output contract, permission grant, Query Store text execution, `LAST_QUERY_PLAN_STATS` enablement, or plan-cache lookup is included.

### Accepted query and plan content

Query text, statement text, scalar expressions, non-parameter constants, object names, missing-index recommendations, warnings, memory grants, and optimizer statistics usage are permitted diagnostic output.

This intentionally accepts that ad hoc or non-parameterized query text and plan constants may contain literal values. The protected content is parameter-value metadata contained in Showplan `ParameterList` elements, including compiled and runtime parameter values.

Statistics histogram values remain excluded because `range_high_key` contains actual indexed-column values.

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
- plan type and other universally available diagnostic plan metadata
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

- `plan_id`
- `query_id`
- `query_hash`
- `query_plan_hash`
- full `query_sql_text`
- engine and compatibility versions
- compile metadata
- trivial, parallel, forced-plan, force-failure, plan-type, dispatcher, and query-variant metadata when available through universally supported Azure SQL columns
- first and last execution metadata when available
- `SanitizationStatus`
- `SanitizationErrorCode`
- `SanitizedShowPlanXml`

The entire multi-statement Showplan document is preserved. There is no separate allowlisted `PlanDiagnosticsXml`.

#### Showplan sanitization

The raw Query Store plan must never be returned. The procedure:

1. copies `query_plan` into a local `xml` variable;
2. counts all elements whose local name is `ParameterList`, regardless of namespace;
3. removes every such element using a bounded XML DML loop;
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
| `@TableName sysname = NULL` | Optional exact table-name filter under the database collation. |
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

Slow-query audit metadata includes resolved UTC window, ordering mode, `@Top`, `@Offset`, `@MinExecutions`, and query-text filter presence/length, but never the filter text.

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

## Schema and rollout

- Add all three procedures under `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs`.
- Add an idempotent role and individual permission migration.
- Introduce all three procedures and the role in one Azure SQL database schema version and migration diff.
- Deploy the schema objects consistently across supported environments; control role membership per environment.
- Use normal repository code review and automated/manual validation. No separate security-review gate is required.
- Do not add self-hosted/on-premises SQL Server support in this baseline.
- Keep actual-plan diagnostics as a documented future feature only.

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
