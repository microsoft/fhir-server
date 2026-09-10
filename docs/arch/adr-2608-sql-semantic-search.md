# ADR-2608: SQL Direct-Text Semantic Search

**Status**: Proposed (direct-text MVP implemented)
**Date**: 2026-08-23
**Revised**: 2026-09-10
**Feature**: SQL semantic search

## Context

FHIR semantic search needs durable embedding metadata and vectors without moving resource identity,
authorization, or transaction ownership outside the SQL data layer. The storage contract is fixed at
SQL native `vector(1536)` with cosine distance. The application feature must remain disabled by default, and existing resource-write callers must
continue to work.

The released schema generator parses native vector columns but emits a `VectorColumn` descriptor that
is not present in the released SQL model package. Depending on an unreleased local package would make
the feature impossible to restore and build independently.

## Options Considered

1. **External vector store** - duplicates FHIR identity, transaction, and authorization boundaries. *(rejected)*
2. **Generator-supported binary or text storage** - builds with released tooling but changes the required persisted vector contract. *(rejected)*
3. **Native SQL vectors with local schema-model compatibility** - retain `vector(1536)`, use text only as the TVP transport, and provide the missing released-generator descriptor locally. *(chosen)*

## Decision

Add schema versions 117 through 119 for the embedding model registry, vector search parameter table
and TVP, merge/reindex/delete procedures, source dependency index, and refresh wrappers. Refresh
enqueue parameters default to false and remain uncalled.

Build with officially released `Microsoft.Health.*` packages. Supply a narrow local `VectorColumn`
schema descriptor for generated column-name metadata, while vector values cross stored-procedure
boundaries as JSON text and are explicitly cast to native `vector(1536)` in SQL. Schema installation
fails before vector DDL on engines other than SQL Server 2025, Azure SQL Database, or Azure SQL
Managed Instance.

The first application layer supports only active `special` SearchParameters configured with
`directText`. It extracts FHIRPath text, chunks and embeds synchronously with Azure Foundry, and
persists vectors in the ordinary resource transaction. Reindex and watchdog recovery rebuild
vectors through the same indexing contract. Ordinary FHIR search continues to own structured
filters and authorization; SQL adds native cosine ranking, deterministic continuation paging, and
standard `Bundle.entry.search.score`.

Reject Binary/PDF sources and semantic chains before embedding calls. Do not install the Patient
semantic operation, evidence extensions, source-refresh worker, or evidence authorization filter
until those capabilities can be added coherently.

## Consequences

- The foundation restores and builds without machine-specific feeds or unreleased packages.
- Native vector storage and normal resource transaction semantics remain unchanged.
- Existing callers remain valid because new enqueue flags default to false and SQL Server treats an
  omitted input TVP as an empty table. New callers send vector rows only to schema 117 or later.
- Deployments must use an engine with native vector support before upgrading to schema version 117 or later.
- Enabled deployments make embedding calls synchronously on writes, reindex, transaction recovery,
  and semantic queries; Foundry availability therefore participates directly in those request and
  recovery failure modes.
- Binary/PDF extraction, linked-source refresh, semantic chains, evidence output, and the Patient
  operation remain deferred.
