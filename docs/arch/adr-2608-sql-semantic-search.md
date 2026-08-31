# ADR-2608: FHIR-Native SQL Semantic Search

**Status**: Proposed
**Date**: 2026-08-23
**Feature**: SQL semantic search

## Context

FHIR search provides deterministic filtering but does not rank narrative passages by conceptual similarity. Semantic retrieval must preserve FHIR authorization, patient and resource boundaries, SearchParameter lifecycle behavior, transactional resource consistency, and inspectable evidence. It must also support direct resource text and text held in a referenced local Binary without hard-coding resource types or parameter identities.

The SQL implementation targets SQL Server 2025 native vectors and a fixed 1536-dimensional embedding contract. The initial workload is bounded by structured and Patient-compartment filters, so exact cosine ranking is preferred over a preview approximate index. The FHIR server returns retrieval results and evidence; clinical answer generation remains outside the server.

## Options Considered

1. **External vector database and proprietary retrieval API** - rejected: duplicates FHIR identity, authorization, lifecycle, and transaction boundaries.
2. **SQL vectors with resource-specific extraction code** - rejected: every new resource type or field would require server code and deployment.
3. **SearchParameter-driven SQL vectors integrated with FHIR search** - chosen: live FHIR metadata defines eligibility and extraction while the existing search pipeline preserves deterministic constraints.

## Decision

Use active, supported, searchable FHIR SearchParameter resources of type `special`, with a Microsoft vector configuration extension, as the source of truth for vector indexing and query behavior. Keep the feature SQL-only and disabled by default. Generate embeddings synchronously during normal writes, replace vectors transactionally with the current resource, support vector-aware `$reindex`, and enqueue durable owner refresh work when a referenced source changes or is deleted.

Apply ordinary FHIR filters, Patient compartments, resource authorization, and linked evidence authorization before returning results. Rank the bounded candidate set with exact cosine distance in SQL Server 2025. Return whole FHIR resources with `Bundle.entry.search.score` and evidence containing the winning passage, SearchParameter canonical, source path, versioned source, and an optional chained witness. Support ordinary resource search, one-level forward or reverse semantic chains, and a custom Patient semantic-search operation whose eligible types are derived from Patient-compartment and vector SearchParameter metadata.

## Consequences

- New semantic targets can be introduced through SearchParameter metadata and lifecycle operations instead of resource-type code changes.
- Structured filtering, authorization, vector ranking, source provenance, and resource persistence share the existing FHIR and SQL ownership boundaries.
- Synchronous embedding increases write latency and makes embedding availability part of successful-write availability.
- Local Binary references require Bundle dependency ordering, fail-closed evidence authorization, durable linked-source refresh jobs, and reindex queue hosting.
- The current storage contract is fixed at `vector(1536)` and cosine distance; changing dimensions requires schema and backfill work.
- Exact kNN is appropriate only while deterministic filters keep candidate sets bounded; approximate indexing remains future work.
- Cosmos DB, asynchronous indexing, multi-hop semantic chains, OCR, and clinical answer generation are outside this decision.