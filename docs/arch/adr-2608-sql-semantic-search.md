# ADR-2608: FHIR-Native SQL Semantic Search

**Status**: Proposed (first milestone implemented; SQL-only, disabled by default)
**Date**: 2026-08-23
**Revised**: 2026-08-31
**Feature**: SQL semantic search

## Context

FHIR search provides deterministic filtering but does not rank narrative chunks by conceptual similarity. Semantic retrieval must preserve FHIR authorization, patient and resource boundaries, SearchParameter lifecycle behavior, transactional resource consistency, and inspectable evidence. It must also support direct resource text and text held in a referenced local Binary without hard-coding resource types or parameter identities.

The SQL implementation targets SQL Server 2025 native vectors and a fixed 1536-dimensional embedding contract. The initial workload is bounded by structured and Patient-compartment filters, so exact cosine ranking is preferred over a preview approximate index. The FHIR server returns retrieval results and evidence; clinical answer generation remains outside the server.

## Options Considered

1. **External vector database and proprietary retrieval API** - rejected: duplicates FHIR identity, authorization, lifecycle, and transaction boundaries.
2. **SQL vectors with resource-specific extraction code** - rejected: every new resource type or field would require server code and deployment.
3. **SearchParameter-driven SQL vectors integrated with FHIR search** - chosen: live FHIR metadata defines eligibility and extraction while the existing search pipeline preserves deterministic constraints.

## Decision

Use active, supported, searchable FHIR SearchParameter resources of type `special`, with a Microsoft vector configuration extension, as the source of truth for vector indexing and query behavior. Keep the feature SQL-only and disabled by default. Generate embeddings synchronously during normal writes, replace vectors transactionally with the current resource, support vector-aware `$reindex`, and enqueue durable owner refresh work when a referenced source changes or is deleted.

Apply ordinary FHIR filters, Patient compartments, resource authorization, and linked evidence authorization before returning results. Rank the bounded candidate set with exact cosine distance in SQL Server 2025. Return whole FHIR resources with `Bundle.entry.search.score` and evidence containing the winning chunk, SearchParameter canonical, source path, versioned source, and an optional chained witness. Expose the capability through two surfaces: a `semantic-text` SearchParameter of type `special` bearing the Microsoft `vector-search-config` extension on ordinary resource search, and a Patient-scoped `$semantic-search` operation (`POST [base]/Patient/{id}/$semantic-search`) whose eligible types derive from Patient-compartment and vector SearchParameter metadata. Support one-level forward or reverse semantic chains, and carry per-match evidence in the `semantic-search-evidence` extension. Drive per-parameter extraction, source strategy (direct text or a referenced local Binary), chunking, minimum score, and distance metric from the `vector-search-config` extension; chunking defaults and query limits come from `VectorSearchConfiguration`.

## Consequences

- New semantic targets can be introduced through SearchParameter metadata and lifecycle operations instead of resource-type code changes.
- Structured filtering, authorization, vector ranking, source provenance, and resource persistence share the existing FHIR and SQL ownership boundaries.
- Synchronous embedding increases write latency and makes embedding availability part of successful-write availability.
- Local Binary references require Bundle dependency ordering, fail-closed evidence authorization, durable linked-source refresh jobs, and reindex queue hosting.
- The current storage contract is fixed at `vector(1536)` and cosine distance; changing dimensions requires schema and backfill work.
- Exact kNN is appropriate only while deterministic filters keep candidate sets bounded; approximate indexing remains future work.
- Cosmos DB, asynchronous indexing, multi-hop semantic chains, OCR, HAS-supplied code-metadata filtering, intent detection, and clinical answer generation are outside this decision.

## Known Limitation — Build-Time Code Generation Dependency (Blocker)

The SQL vector tables declare a `vector(1536)` column. Generating the SQL schema model classes requires `Microsoft.Health.Extensions.BuildTimeCodeGenerator` to recognize the `vector` column type. The released generator does not, so this feature currently depends on an unreleased change to the shared `healthcare-shared-components` code generator: PR [microsoft/healthcare-shared-components#1449](https://github.com/microsoft/healthcare-shared-components/pull/1449), "Support VECTOR columns in generated SQL schema models", developed on branch `users/t-annag/fix-vector-schema-model` (2 commits over tag `v11.0.128`).

That shared-components change is not expected to merge or ship as an official package. Consequently:

- The feature cannot be merged into `microsoft/fhir-server` main while it hard-depends on the unreleased generator, because upstream CI can only restore officially released `Microsoft.Health.*` packages.
- The branch builds only against a locally built preview package set (for example `1.0.0-fix-vector-schema-model-20260720-144944-preview`), built from PR #1449's branch and served from a local NuGet feed. `HealthcareSharedPackageVersion` is pointed at that preview for local and demo builds and must be reverted to an official version before any upstream PR.
- The feature is otherwise complete and demonstrable end-to-end against the local preview packages.

### Recommended path to unblock (future work)

Decouple the feature from the code generator so it builds with the released packages. Two options:

- Exclude the vector tables from generated-model production and hand-author the required row and table model types, or
- Represent the vector column with a generator-supported type in the model layer and cast to `vector(1536)` in SQL at query and stored-procedure time.

Either approach removes the dependency on the unreleased shared-components change and makes the feature independently mergeable.

## References

- Design spec: "Semantic Search over FHIR Clinical Documents" (`health-paas-docs`, PR 65041) — the in-depth companion to this ADR.
- ADR 2605: Vector Search Parameter.
- Build-time generator dependency: [microsoft/healthcare-shared-components#1449](https://github.com/microsoft/healthcare-shared-components/pull/1449), "Support VECTOR columns in generated SQL schema models" (branch `users/t-annag/fix-vector-schema-model`).
- `docs/SchemaVersioning.md`: SQL schema version and migration rules (this feature adds schema versions 117-119).
- SQL Server 2025 vector data type and `VECTOR_DISTANCE` (Microsoft Learn).
- Azure OpenAI / Azure AI Foundry embeddings (Microsoft Learn).