# SQL Server search architecture

The SQL Server backend translates shared search expressions into SQL predicates, statements, and common table expressions (CTEs).
The [search architecture](SearchArchitecture.md) describes extraction and shared expression parsing.
The [SQL Server search flow](flow%20diagrams/search-sql-server.md) shows query execution and result construction.

## SQL generation

`SqlServerSearchService` passes a rewritten `SqlRootExpression` to `SqlQueryGenerator`, which emits the SQL
statements and CTEs for the search. The generator uses `EXISTS` for source-existence checks when practical.
For complex searches or optimizer retries, it can use joins instead. The final query applies resource-version
and deletion filters, sorting, and pagination, then returns distinct resources. Include searches save the
matched resources before generating queries for included resources.

Independent chained parameters can match different referenced resources. When the generator uses joins,
it intersects independent chains, and root-level filters that follow a chain, on distinct source-resource keys
so multiple matches in one chain do not multiply rows in later tables.

### Query shape comments

Generated SQL includes a value-free FHIR query shape. When the generator emits its existing parameter hash
comment with SQL query-plan reuse disabled, the shape is added without changing that comment's location:

```sql
/* HASH <parameter-hash> params=@p0,@p1 fhir=Patient?birthdate&name */
```

When no parameter hash comment is emitted, including when query-plan reuse is enabled or there are no
parameters to hash, the generator inserts only a shape comment at the equivalent in-statement location:

```sql
WITH cte0 AS (...)
/* fhir=Patient?birthdate&name */
SELECT ... FROM cte0
```

For shape-only queries without a CTE, the comment follows `SELECT`, including count queries. Include queries
annotate both the filtering `INSERT ... SELECT` statement and the subsequent resource-selection statement.
No standalone shape comment is prepended to the batch or appended after a statement. Query Store retains
the in-statement shape-only comments, so the existing slow-query text lookup can match those statements.

The generator records insertion positions without changing the SQL. After SQL simplification, the internal
custom-query hash is calculated from exactly the unannotated SQL, then the shape annotations are inserted.
Parameter-value hashing and custom-query lookup are unchanged.

With query-plan reuse enabled, identical SQL and normalized shapes remain identical across parameter-value
and input-name-order changes. Different shapes produce different SQL text and may use separate plan-cache
entries even with reuse enabled. The shape-only comment does not add a parameter-value hash.

`SearchOptionsFactory` creates the shape from the search scope and the parsed query parameter names
already supplied to the search pipeline. Ordinary searches use `Patient?...`, history searches use
`Patient/_history?...`, and compartment searches omit the compartment ID and use
`Patient/$compartment/Observation?...`. Names are sorted using ordinal ordering and repeated names are retained,
so changing parameter values or query-string order does not change the annotation. Parameter values and
compartment IDs are never included. Unsafe comment and control characters are replaced. Output longer than 1024
characters is truncated to 1023 characters plus `~`.

This SQL generation path is used by ordinary, compartment, and history searches, including the internal
search phases used by Patient `$everything`. The annotation describes the generated target-resource query
shape. It does not attempt to identify the originating HTTP operation. Include and reverse-include syntax is
preserved when those parameter names are present.

## Instant date predicates

`FhirSqlServer:EnableInstantDateTimeRewriter` enables single-column date predicates for model-proven FHIR `instant` search parameters.
The setting defaults to `false`. Its environment variable is `FhirSqlServer__EnableInstantDateTimeRewriter`.
It also requires `FhirServer:CoreFeatures:EnableSearchParameterTemporalClassification` to be enabled at startup
(environment variable `FhirServer__CoreFeatures__EnableSearchParameterTemporalClassification`).
If the SQL setting is enabled while Core classification is off, the server logs a startup warning and uses
the existing SQL. The settings are independent of `EnableFhirDateContainment` and `EnableScalarTemporalEqualityRewriter`.

The instant converter creates equal UTC `Start` and `End` values, including fractional seconds.
The date row generator passes both endpoints through the precision-7 table-valued parameter (TVP).
Merge SQL stores them in `StartDateTime` and `EndDateTime` as `datetime2(7)`.
The rewrite substitutes `StartDateTime` for `EndDateTime` in eligible predicates.
Operators, boundaries, Boolean structure, and chain scope stay unchanged.

Eligibility requires a system-defined scalar date parameter with derived `TemporalType.Instant` metadata
whose complete definition is a resource-qualified property path, not a union.
Core [classifies temporal types during definition loading](SearchArchitecture.md#derived-temporal-type-metadata)
using the active FHIR model. The SQL rewriter consumes that result rather than walking model metadata per query.
The model must resolve every property with exact casing, without choices or mixed types, to a terminal `instant`.
For example, the built-in `Slot-start` parameter uses `Slot.start`.
An instant-valued path alone does not establish eligibility. The path must belong to a qualifying built-in search parameter.
The query literal's precision does not establish eligibility.

Custom parameters retain their existing SQL and are not classified, even when their current path resolves to `instant`.
Mutable definitions can leave stale definition caches and index rows on other instances.
Dates, dateTimes, Periods, composites, unresolved paths, unions, functions, and casts also retain their existing SQL.
Even homogeneous instant unions are excluded: shared type knowledge is broader than this optimization's policy.
An unclassified definition remains `Unknown` and is not rewritten. If the classifier's supported expression
grammar expands in the future, SQL eligibility must be reviewed separately rather than inferred from the leaf type alone.

`InstantDateTimeRewriter` runs after `DateTimeTableExpressionCombiner` and before bounded-range expansion.
For example, `Slot?start=ge2026-10-01&start=lt2026-10-02&schedule.actor=Location/example` keeps its combined date range but uses `StartDateTime` for both bounds.
That range no longer needs the date-bounded-range union. The actor chain and its joins do not change.

This optimization changes SQL predicates only. Both stored columns and all existing indexes remain.
It requires no schema migration or reindexing and does not change Cosmos DB queries.
