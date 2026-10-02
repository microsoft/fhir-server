# ADR-2608: SQL Semantic Search Foundations

**Status**: Proposed
**Date**: 2026-08-23
**Revised**: 2026-09-23
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

### Engine compatibility alternatives

Because the chosen storage contract is the native `vector` type, V118 cannot install on an engine
that does not provide it. Three ways to avoid that were considered and rejected:

4. **Conditional installation via dynamic SQL** - create the vector table, table type and the
   vector-touching procedure bodies inside `EXEC sp_executesql` behind the `sys.types` probe, with
   non-vector variants of `MergeResources` and `UpdateResourceSearchParams`. Schema would install
   everywhere and semantic search would simply be unavailable on older engines. Rejected: it
   requires maintaining two shapes of the hottest write procedures indefinitely, and schema version
   would no longer imply vector capability, so every caller check would need a runtime probe.
   *(rejected)*
5. **Portable storage column** - persist `Embedding` as `varbinary(max)` in V118 and add the native
   column later. Rejected: it abandons the fixed native-vector storage contract that option 3 was
   chosen to preserve, and defers the same decision into the query layer. *(rejected)*
6. **Separate opt-in enablement script** - keep V118 vector-free and ship vector objects as an
   explicitly executed operation. Rejected: the merge procedures still need dual shapes, so it
   carries option 4's cost without its benefit. *(rejected)*

## Decision

Add one schema version, 118, for the embedding model registry, vector search parameter table and TVP,
and vector support in the existing merge, reindex, and hard-delete procedures. Persist the exact source
passage as compressed bytes with its SHA-256 hash; do not retain plaintext or source-provenance columns.
Reindex callers identify the evaluated resource subset explicitly so an omitted subset preserves vectors
while an evaluated resource with no vector rows removes stale vectors.

Include model identity in the clustered primary key and the vector TVP's uniqueness constraint:
`(ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal)`.
This permits model-distinct rows without changing the single-model MVP. Application deduplication
uses the same key. Side-by-side indexing and model migration remain follow-up work because
reindex replacement and merge recovery still operate at resource scope.

Build with officially released `Microsoft.Health.*` packages. Supply a narrow local `VectorColumn`
schema descriptor for generated column-name metadata, while vector values cross stored-procedure
boundaries as JSON text and are explicitly cast to native `vector(1536)` in SQL. The V118 migration
probes `sys.types` and fails before DDL when the native vector type is unavailable. The shared
initialization script carries no such probe: a fresh install is already guarded by the vector DDL
itself, which fails inside the initialization transaction on an engine without the type.

Accept the resulting engine requirement rather than working around it: schema V118 requires
Azure SQL Database, or SQL Server 2025 or later. Local development and integration testing move to
SQL Server 2025 accordingly.

Retain the nonpartitioned native-vector table and clustered primary key as the foundation for a
possible later DiskANN index. Do not create an approximate index in the schema migration or treat
native-vector availability as proof of DiskANN availability. The
[Azure SQL compatibility assessment](../SqlVectorIndexCompatibility.md) records the documented
General Purpose and Hyperscale baseline, preview limitations, and a General Purpose execution of
the exact table layout with a version-3 DiskANN index. Other offerings and production ANN query
semantics still require execution validation.

## Consequences

- **Deployments on SQL Server 2019 and 2022 cannot reach schema V118 and remain pinned at V117.**
  With `SchemaVersionConstants.Min` at V113 those deployments stay supported, but they cannot take
  V118 or any later schema version until the engine is upgraded. This is a deliberate
  supportability commitment, not an incidental migration failure.
- Contributors need a vector-capable engine for local work: the Docker samples pin
  `mcr.microsoft.com/mssql/server:2025-latest`, and the integration test fixture's default
  `server=(local)` instance must be SQL Server 2025 or later.
- The foundation restores and builds without machine-specific feeds or unreleased packages.
- Vector replacement shares the existing reindex transaction, version checks, failure count, and rollback behavior.
- Existing callers remain valid because omitted input TVPs behave as empty tables.
- Model-distinct keys do not provide a model-preserving reindex or backfill/cutover workflow.
- Databases created by experimental builds of this feature, which numbered the vector schema 117
  through 119, cannot upgrade in place and must be recreated. V117 is now the unrelated
  `GetMostRecentJob` migration, which such databases would otherwise skip.
- Persisted passage bytes are not consumed by score-only MVP queries.
- Extraction, embedding calls, vector queries, and FHIR response behavior remain owned by the application layer.
- DiskANN remains a future, explicit approximate-query choice; the current schema does not certify
  the composite-key and PAGE-compressed layout on every Azure SQL tier or index version.
