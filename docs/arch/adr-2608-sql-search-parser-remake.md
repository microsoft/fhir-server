# ADR-2608: SQL Search Parser Remake

**Status**: Proposed
**Date**: 2026-08-21
**Feature**: SqlSearchParser

## Context

The FHIR server's SQL search query generation pipeline relied on an expression tree architecture where incoming search parameters were first parsed into a generic `Expression` tree, then passed through 14+ chained rewriter/visitor passes (compartment rewriting, date equality semantics, flattening, untyped reference resolution, sort rewriting, partition elimination, predicate pushdown, string overflow handling, numeric range rewriting, include seeding, and more). The final `SqlQueryGenerator` visitor then converted the fully-rewritten expression tree into parameterized SQL, optionally cached as a stored procedure via `CustomQueries`.

This approach had several problems:

- **Debuggability**: Tracing how a FHIR search URL became a SQL query required stepping through 14+ visitor passes, each mutating the expression tree in non-obvious ways. Intermediate states were opaque and difficult to inspect.
- **Complexity**: Each new search feature (chained searches, reverse chains, SMART scopes, compartments) required adding or modifying rewriter passes that interacted with all other passes, creating a combinatorial explosion of edge cases.
- **Indirection**: The expression tree abstraction was designed to be storage-agnostic, but in practice the SQL Server backend was the only consumer. The abstraction added layers of indirection without practical benefit.
- **Performance tuning**: The generated SQL was constrained by what the visitor pattern could express. Optimizations like sharing expensive reference CTEs across multiple chain parameters were architecturally difficult to implement.

## Options Considered

1. **Incremental refactoring of the expression tree pipeline** — Simplify existing rewriters and improve logging *(rejected: the fundamental problem is the multi-pass visitor architecture itself; incremental fixes would not address debuggability or the indirection cost)*

2. **Direct SQL generation from query parameters** — Bypass the expression tree entirely and generate CTEs directly from the parsed query parameters using type-specific SQL parsers *(viable)*

3. **Replace expression tree with a SQL-specific IR** — Keep the expression parsing but introduce a SQL-specific intermediate representation before generation *(rejected: still two translation layers when one suffices; the query parameters already carry all needed information)*

## Decision

We chose **direct SQL generation from query parameters** (Option 2). The new `SearchParameterSqlParser` in `SqlSearchParser/` takes `QueryParams` (a dictionary of search parameter names to values) directly from `SearchOptionsFactory` and produces a raw SQL query string composed of CTEs.

The new pipeline flow is:

```
HTTP Request
  → SearchOptionsFactory (parses URL into QueryParams dictionary)
  → SearchParameterSqlParser.ParseMultiple (generates SQL directly)
  → CTE-based SQL query string
  → SqlConnection.ExecuteReader
```

Each search parameter type has a dedicated parser (`DateTimeSqlParser`, `TokenSqlParser`, `ReferenceSqlParser`, `StringSqlParser`, etc.) that knows how to generate the appropriate CTE for its table. Special parsers handle cross-cutting concerns: `ChainedSqlParser` for forward chains, `ReversedChainSqlParser` for reverse chains, `CompartmentSqlParser` for compartment searches, `SmartCompartmentSqlParser` for SMART scopes, and `IncludeSqlParser` for `_include`/`_revinclude`.

Key architectural features of the new approach:

- **Chain grouping**: Multiple chain parameters sharing the same reference lookup are grouped via `ChainSearchGroup`, allowing the expensive reference CTE to be generated once and reused. An intersection CTE enforces AND semantics across grouped chains.
- **Linear CTE pipeline**: Each parser appends its CTE to a `SqlQueryBuilder`, with `LastCteName` threading results forward. No multi-pass rewriting needed.
- **Direct SQL control**: Optimizations like sort-aware paging, continuation token handling, and partition elimination are applied inline during generation rather than as separate visitor passes.

## Consequences

- **Debuggability is dramatically improved.** A standalone `SqlSearchDebugger` tool (in `tools/`) can show the mapping from FHIR URL to SQL query without connecting to a database. The single-pass generation makes it straightforward to trace how each parameter contributes to the final query.
- **New search features are easier to add.** Adding SMART scope support, for example, required writing one new parser class (`SmartCompartmentSqlParser`) and a few lines in `ParseMultiple`, rather than inserting a new rewriter into a 14-pass chain.
- **Performance optimizations are more natural.** Chain grouping with shared reference CTEs was a direct architectural addition, not a fight against the visitor pattern.
- **The expression tree pipeline is retained but dormant.** The old `CreateDefaultSearchExpression` method and its rewriters remain in the codebase (commented out) as a fallback reference. Some expression-based validation (e.g., SMART scope type checking in `ExpressionAccessControl`) still operates on expressions built by `SearchOptionsFactory`.
- **Storage abstraction is reduced.** The new parser is SQL Server-specific by design. If a second storage backend needed the same search semantics, it would need its own query generator rather than reusing the expression tree. In practice, this trade-off is acceptable since the Cosmos DB backend has its own query pipeline already.
- **The old `SqlQueryGenerator`, all 14+ rewriter classes, and the `CustomQueries` stored procedure cache are no longer exercised.** These can be removed once the new parser is validated in production.
