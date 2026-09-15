# ADR-2608: SQL Semantic Search Foundations

**Status**: Proposed
**Date**: 2026-08-23
**Revised**: 2026-09-14
**Feature**: SQL semantic search

## Context

FHIR semantic search needs durable embedding metadata, compressed source passages, and vectors without
moving resource identity, authorization, or transaction ownership outside the SQL data layer. The
storage contract is fixed at SQL native `vector(1536)` with cosine distance. Existing resource-write
and reindex callers must continue to work when they omit the new table-valued parameters.

The released schema generator parses native vector columns but emits a `VectorColumn` descriptor that
is not present in the released SQL model package. Depending on an unreleased local package would make
the feature impossible to restore and build independently.

## Options Considered

1. **External vector store** - duplicates FHIR identity, transaction, and authorization boundaries. *(rejected)*
2. **Generator-supported binary or text storage** - builds with released tooling but changes the required persisted vector contract. *(rejected)*
3. **Native SQL vectors with compressed passages and local schema-model compatibility** - retain `vector(1536)`, use JSON text only for vector transport, and provide the missing released-generator descriptor locally. *(chosen)*

## Decision

Add one schema version, 117, for the embedding model registry, vector search parameter table and TVP,
and vector support in the existing merge, reindex, and hard-delete procedures. Persist the exact source
passage as compressed bytes with its SHA-256 hash; do not retain plaintext or source-provenance columns.
Reindex callers identify the evaluated resource subset explicitly so an omitted subset preserves vectors
while an evaluated resource with no vector rows removes stale vectors.

Build with officially released `Microsoft.Health.*` packages. Supply a narrow local `VectorColumn`
schema descriptor for generated column-name metadata, while vector values cross stored-procedure
boundaries as JSON text and are explicitly cast to native `vector(1536)` in SQL. Schema installation
probes `sys.types` and fails before DDL when the native vector type is unavailable.

Retain the nonpartitioned native-vector table and clustered primary key as the foundation for a
possible later DiskANN index. Do not create an approximate index in the schema migration or treat
native-vector availability as proof of DiskANN availability. The
[Azure SQL compatibility assessment](../SqlVectorIndexCompatibility.md) records the documented
General Purpose and Hyperscale baseline, preview limitations, and the exact table combinations that
still require execution validation.

## Consequences

- The foundation restores and builds without machine-specific feeds or unreleased packages.
- Vector replacement shares the existing reindex transaction, version checks, failure count, and rollback behavior.
- Existing callers remain valid because omitted input TVPs behave as empty tables.
- Experimental schema versions 117 through 119 cannot upgrade in place to the consolidated schema.
- Persisted passage bytes are not consumed by score-only MVP queries.
- Extraction, embedding calls, vector queries, and FHIR response behavior remain owned by the application layer.
- DiskANN remains a future, explicit approximate-query choice; the current schema does not certify
  the composite-key and PAGE-compressed layout on every Azure SQL tier or index version.
