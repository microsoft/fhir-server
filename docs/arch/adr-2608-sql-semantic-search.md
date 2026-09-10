# ADR-2608: SQL Semantic Search Foundations

**Status**: Proposed (schema foundation implemented)
**Date**: 2026-08-23
**Revised**: 2026-09-10
**Feature**: SQL semantic search

## Context

FHIR semantic search needs durable embedding metadata and vectors without moving resource identity,
authorization, or transaction ownership outside the SQL data layer. The storage contract is fixed at
SQL native `vector(1536)` with cosine distance. The schema must remain inert until later application
layers opt into extraction and querying, and existing resource-write callers must continue to work.

The released schema generator parses native vector columns but emits a `VectorColumn` descriptor that
is not present in the released SQL model package. Depending on an unreleased local package would make
the feature impossible to restore and build independently.

## Options Considered

1. **External vector store** - duplicates FHIR identity, transaction, and authorization boundaries. *(rejected)*
2. **Generator-supported binary or text storage** - builds with released tooling but changes the required persisted vector contract. *(rejected)*
3. **Native SQL vectors with local schema-model compatibility** - retain `vector(1536)`, use text only as the TVP transport, and provide the missing released-generator descriptor locally. *(chosen)*

## Decision

Add schema versions 117 through 119 for the embedding model registry, vector search parameter table and
TVP, merge/reindex/delete procedures, source dependency index, and refresh wrappers. Refresh enqueue
parameters default to false, and no worker or application service is registered in this layer.

Build with officially released `Microsoft.Health.*` packages. Supply a narrow local `VectorColumn`
schema descriptor for generated column-name metadata, while vector values cross stored-procedure
boundaries as JSON text and are explicitly cast to native `vector(1536)` in SQL. Until the indexing
layer is added, ordinary resource writes send an empty vector TVP. Schema installation fails before
vector DDL on engines other than SQL Server 2025, Azure SQL Database, or Azure SQL Managed Instance.

## Consequences

- The foundation restores and builds without machine-specific feeds or unreleased packages.
- Native vector storage and normal resource transaction semantics remain unchanged.
- Existing callers remain valid because new enqueue flags default to false and SQL Server treats an
  omitted input TVP as an empty table. New callers still send an explicit empty vector TVP until the
  indexing layer is present.
- Deployments must use an engine with native vector support before upgrading to schema version 117 or later.
- Extraction, embedding calls, vector queries, refresh processing, and FHIR response behavior remain owned by later layers.
