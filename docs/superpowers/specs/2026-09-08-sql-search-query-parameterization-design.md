# SQL Search Query Parameterization Design

## Context

The new SQL search parser currently embeds request values directly in generated SQL. Although those values are escaped, embedding them prevents same-shape searches with different values from sharing SQL text and therefore limits SQL Server query-plan reuse.

The retired expression-based generator used `SqlQueryParameterManager` and `HashingSqlQueryParameterManager` to:

- Bind searchable and paging values as `SqlParameter` instances.
- Infer SQL types from generated `VLatest` schema-column metadata.
- Keep structural `ResourceTypeId`, `ReferenceResourceTypeId`, and `SearchParamId` values inline.
- Exclude TOP/count, continuation, and selected resource-ID values from the parameter hash.
- Append a value-hash comment when query-plan reuse was disabled.

The new parser should restore those behaviors without restoring the expression model.

## Decision

Pass a request-scoped `HashingSqlQueryParameterManager` through the new parser pipeline. The manager will wrap the active `SqlCommand.Parameters` collection, preserving the old parameter naming, typing, exclusion, and hashing behavior.

`SqlServerSearchService` will create the parameter manager after creating each search command and pass it to `SearchParameterSqlParser.ParseMultiple` together with the effective query-plan reuse decision. `ParserOptions` will carry the manager through all nested parser operations.

Low-level parsers will bind values with the relevant `VLatest` column metadata. Their SQL clauses will contain parameter names such as `@p0` instead of serialized request values. Structural identifiers that `HashingSqlQueryParameterManager` intentionally returns as literals will remain inline.

## Parameter Policy

The implementation will match the old generator's selective behavior:

| Value category | SQL representation | Included in value hash |
|---|---|---|
| Search values | Parameter | Yes |
| Reference resource IDs | Parameter | Yes |
| `_id` resource IDs | Parameter | No |
| Compartment owner resource ID | Parameter | No |
| Sort continuation value | Parameter | No |
| Continuation surrogate ID | Parameter | No |
| TOP/count/include limits | Parameter | No |
| Include truncation and row-limit predicate values | Parameter | Yes |
| `ResourceTypeId` | Literal | No |
| `ReferenceResourceTypeId` | Literal | No |
| `SearchParamId` | Literal | No |

Parameter reuse will mirror the old generator. Sort-continuation predicates receive separate parameters for separate comparisons, while SMART compartment predicates reuse the single owner-ID parameter created for that membership rule. Each use retains its original hash policy; for example, an include count used by `TOP` is excluded from the hash while the same count used by the partial-result predicate is included.

String LIKE patterns will be placed in parameter values rather than concatenated into SQL. Existing wildcard semantics and escaping will be retained.

## Query-Plan Reuse Policy

When reuse is enabled, same-shape searches with different values will generate identical SQL text and different command parameter values.

When reuse is disabled and hashable parameters exist, the parser will append the existing `/* HASH ... */` comment using `HashingSqlQueryParameterManager`. The comment causes distinct values to produce distinct SQL text and prevents unintended plan reuse.

`SqlQueryHashCalculator` will continue removing the hash comment before calculating the custom-query hash. SMART include searches will preserve the old scope-specific parameter hash behavior where applicable.

## Search Flow

1. `SqlServerSearchService` creates a `SqlCommand`.
2. It wraps the command's parameter collection in `SqlQueryParameterManager` and `HashingSqlQueryParameterManager`.
3. It calls `SearchParameterSqlParser.ParseMultiple` with the request parameters, search options, continuation tokens, parameter manager, and reuse decision.
4. Every parser adds runtime values to the manager while appending only returned parameter names to SQL.
5. The orchestrator appends the value-hash comment when policy requires it.
6. The service performs custom-query lookup using the normalized SQL hash, assigns command text/type, and executes the command with its populated parameters.

Normal searches and include-only searches will use the same flow. `SearchIncludeImpl` will accept and propagate the effective reuse decision rather than dropping it.

## Parser API Changes

`ParserOptions` will expose the request-scoped parameter manager and query-plan reuse state. `BaseSqlParser.BuildWhereClause` and composite parser calls will receive the parser context needed to bind typed values.

`GetSearchJoinInfo` and the combined reverse-chain path will use the same context, ensuring Member Match optimization remains parameterized.

No parser instance will retain request-scoped state. This keeps the registered parser safe for concurrent searches.

## Error Handling

Generation will fail explicitly if a runtime value cannot be mapped to a supported parameter type. It will not fall back to embedding the value in SQL.

Existing invalid-value exceptions and the 2,048-value limit remain unchanged. Null values will use the established parameter manager behavior and will not be serialized as SQL text.

## Testing

Tests will be added before implementation and cover:

- Parameter names in SQL and matching values/types in `SqlCommand.Parameters`.
- Absence of raw request values from SQL for every base parser.
- ID, compartment, SMART, sort, include, chain, reverse-chain, and combined reverse-chain paths.
- Literal treatment of resource-type and search-parameter IDs.
- Hash inclusion and exclusion rules.
- Identical SQL for different values when reuse is enabled.
- Different hash comments for different values when reuse is disabled.
- Reuse-policy propagation through normal and include-only service paths.
- Existing query structure, paging, and parser behavior.

Validation will use the existing build and targeted SQL parser/search unit tests. Any E2E server started during validation will be stopped before completion.

## Alternatives Rejected

### Custom parameter descriptors

Returning a new query object with custom parameter descriptors would separate the parser from SqlClient, but it would duplicate schema typing and hash policy already implemented by the existing managers.

### Literal post-processing

Replacing literals after SQL generation would require parsing SQL text and inferring the intended type and hash behavior of each literal. This is fragile, especially when identical text values occur in predicates with different schema types.

## Consequences

- Same-shape searches can reuse query plans when allowed.
- User-provided values no longer appear in SQL command text or logs.
- Skew-sensitive searches retain the old value-hash isolation behavior.
- Parser method signatures and tests require coordinated updates.
- SQL parameter allocation remains request-scoped and concurrency-safe.
