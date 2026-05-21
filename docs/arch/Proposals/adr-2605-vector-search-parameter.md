# ADR 2605: Vector Search Parameter (Semantic / ANN Search over FHIR Text Fields)

Labels: [SQL](https://github.com/microsoft/fhir-server/labels/Area-SQL), [Search](https://github.com/microsoft/fhir-server/labels/Area-Search)

## Status
Proposed — investigation stage. This ADR explores a new kind of search parameter and intentionally leaves several items as **Open Questions** to be resolved before the ADR moves out of `Proposals/`.

## Context
Today the FHIR Server supports only the search parameter value types defined by the FHIR specification — `number`, `date`, `string`, `token`, `reference`, `composite`, `quantity`, `uri`, and `special`. All of these resolve to lexical/structural predicates in SQL. There is no first-class way for a client to ask *"find resources whose clinical note is semantically similar to this query"*.

Clinically meaningful text — `Observation.note.text`, `DocumentReference.content.attachment` (decoded), `Condition.note.text`, free-text portions of `QuestionnaireResponse`, etc. — is one of the highest-signal fields for many real workloads (clinical decision support, cohort discovery, summarization grounding, ambient documentation review). Searching it lexically with `:contains` is brittle, language-dependent, and misses paraphrase.

Two recent platform capabilities make a server-side vector search parameter newly tractable:
1. **Azure SQL Database** has introduced a `VECTOR(N)` type and an Approximate-Nearest-Neighbor (ANN) index based on DiskANN, allowing similarity search to live next to the existing relational search indexes (in preview at the time of writing).
2. **Azure AI Foundry** exposes embeddings endpoints reachable from the FHIR Server pod/VM via **managed identity**, with regional deployment and private-endpoint support — important for PHI workloads.

This ADR proposes how a new "vector" search parameter would be defined, persisted, queried, and operated. It is deliberately scoped to **Azure SQL Database** for v1; Cosmos DB parity is acknowledged as future work.

### Goals
- Allow operators to declare *which* text fields on which resource types should be semantically searchable.
- Let clients perform similarity search through a familiar URL shape, combinable with existing filters (hybrid search).
- Stay FHIR-conformant in how the parameter is *declared* (so external tooling can still read the `SearchParameter` resource), even where the *query syntax* is necessarily a Microsoft extension.
- Keep PHI handling explicit, auditable, and opt-in.

### Non-Goals (v1)
- Cosmos DB persistence.
- Pluggable embedding providers (Azure OpenAI direct, self-hosted, third-party). Foundry only.
- Per-search-parameter model selection. The embedding model and dimension are a server-level configuration.
- Vector search on historical resource versions, soft-deleted resources, or via `_include` / chained / reverse-chained / composite parameters.
- Returning raw embeddings to clients.

## Decision

We will introduce a **Vector Search Parameter** as a new ISearchValue kind, persisted in a new SQL table backed by an Azure SQL vector index, with embeddings produced synchronously by Azure AI Foundry over managed identity at resource write time. A vector SP is declared as a FHIR `SearchParameter` with `type=special` and a Microsoft `vector-search-config` extension; clients query it with the standard `?paramName=text` URL shape, and the parser uses the parameter's declared shape — not a modifier — to interpret the value as a similarity query.

### 1. Search parameter definition

A vector SP is declared as a normal FHIR `SearchParameter` resource so that it appears in `CapabilityStatement.rest.resource.searchParam` and can be loaded by the existing search-parameter cache machinery (see ADR 2603 *Atomic SearchParameter CRUD and Cache-Refresh Ownership* and ADR 2603 *Non-Spec Default Search Parameters*).

- `SearchParameter.type` = `special` — this is the closest FHIR-conformant value for a parameter whose semantics are not covered by the standard types.
- `SearchParameter.expression` = standard FHIRPath expression identifying the source text node(s) (e.g. `Observation.note.text`).
- A Microsoft extension `http://microsoft.com/fhir/StructureDefinition/vector-search-config` on the `SearchParameter` resource carries vector-specific configuration:
  - `kind` — discriminator, fixed to `vector` for v1.
  - `extractionPolicy` — `firstValue` | `concatenate` | `perValueRow` (see §4).
  - `maxInputTokens` — truncation budget per embedding call.

The embedding model name, model version/deployment fingerprint, vector dimension, and distance metric are **not** on the `SearchParameter` — they are server-level configuration. This avoids the situation where two vector SPs declare incompatible dimensions and the SQL schema cannot represent both.

### 2. Query syntax

```
GET /Observation?clinicalNote=patient short of breath at rest&subject=Patient/123&_count=20
```

- **No modifier is required.** The SearchParameter declaring `clinicalNote` carries the `vector-search-config` extension; the parser uses the parameter definition itself to decide the value is a similarity query, exactly the way it uses `type=date` on `birthdate` to decide `?birthdate=2020-01-01` is a date predicate. This keeps the URL surface identical in shape to every other FHIR search and avoids inventing a non-standard `SearchModifierCode` value.
- Combinable with any other search parameter. The combined predicate is a **hybrid** query: ANN over the vector index intersected with the relational predicates produced by the rest of the expression tree.
- Standard modifiers behave predictably on a vector SP:
  - `:missing=true|false` — works as on any SP, returns resources that do/do not have the vectorized text.
  - All other modifiers (`:exact`, `:contains`, `:not`, `:above`, `:below`, type modifiers, etc.) return `400 invalid-modifier` for v1. They may be reclaimed later for specific semantics (e.g. a future `:exact` could fall back to lexical match), but reserving them now would be premature.
- Optional reserved companion parameters (under `_` prefix to avoid colliding with future spec parameters):
  - `_vectorK` — number of nearest neighbors to consider before applying filters. Default server-side, e.g. 200.
  - `_vectorMinScore` — minimum normalized similarity score (0..1) below which results are dropped.
- Result scoring: results carry `Bundle.entry.search.score` populated as a cosine-similarity-derived 0..1 score (higher = more similar). The `Bundle.entry.search.mode` remains `match`.
- Capability advertising: vector SPs appear in `CapabilityStatement.rest.resource.searchParam` like any other parameter; the `vector-search-config` extension on the `SearchParameter` resource is the discoverability mechanism. Clients that read SearchParameter resources (the standard, FHIR-recommended discovery path) will see the extension and know how to query.

### 3. Embedding pipeline (write path)

Resource Create/Update synchronously embeds the extracted text before the SQL write transaction completes:

```
ResourceUpsertHandler
  └─ extract text via FHIRPath (per vector SP defined on the resource type)
  └─ for each non-empty extraction:
       ├─ apply extraction policy (§4)
       ├─ call IEmbeddingClient.EmbedAsync(text)  ← Azure AI Foundry, MI
       └─ stage VectorSearchParam row(s)
  └─ MergeResources sproc commits resource + all search-param rows atomically
```

Key properties:
- All embedding calls for a request happen **before** SQL write locks are acquired. For a transaction bundle of N resources, all N×M embedding calls complete (or any fails) before the bundle's SQL transaction opens, so Foundry latency does not amplify into DB contention.
- Failure mode: embedding call exhaustion (after a bounded retry policy with jittered backoff) fails the FHIR write with `503 Service Unavailable` and an `OperationOutcome` of `transient`. Transaction bundles are all-or-nothing — a single embedding failure rolls back the entire bundle.
- Authentication: `DefaultAzureCredential` chain with managed identity as the intended production credential; no static keys.
- The embedding client is abstracted as `IEmbeddingClient` with a single production implementation `AzureAIFoundryEmbeddingClient`. The interface exists for testability, not provider pluggability.

#### Bulk import (`$import`)

`$import` does **not** use the standard MediatR upsert pipeline. v1 behavior:
- If any vector SP is registered for an imported resource type, `$import` records a per-resource-type warning in the import outcome and **skips** vector row generation. The resource is otherwise imported normally.
- A follow-up `$reindex` on the vector SP is required to populate vector rows for imported resources.
- Rationale: synchronous embedding would dominate the cost/latency of bulk import for marginal benefit. Better to import first, then reindex deliberately.

#### Conditional create / no-op update

When the upsert path determines the resource is unchanged (existing optimization), the existing vector rows are reused — no embedding call is made. A future optimization may compare a stored `SourceTextHash` to short-circuit re-embedding even when other fields changed.

### 4. Text extraction policy

FHIRPath expressions on text fields commonly yield zero, one, or many strings, and individual strings may exceed an embedding model's token limit.

- `firstValue`: take only the first non-empty extraction. Truncate to `maxInputTokens`.
- `concatenate`: join all extractions with a separator into one input. Truncate to `maxInputTokens`. Default for v1.
- `perValueRow`: emit one vector row per extraction (with a `ChunkOrdinal` column). Search returns the resource if **any** row matches.
- Binary attachments (`Attachment.data`) are **not** decoded or embedded in v1. Adding attachment text is future work and likely requires a separate ADR (chunking strategy, max size, async pipeline).
- An extraction yielding only whitespace produces no vector rows; resource is still searchable via other parameters.

### 5. SQL persistence

New table (subject to refinement during implementation):

```sql
CREATE TABLE dbo.VectorSearchParam (
    ResourceTypeId       SMALLINT     NOT NULL,
    ResourceSurrogateId  BIGINT       NOT NULL,
    SearchParamId        SMALLINT     NOT NULL,
    ChunkOrdinal         SMALLINT     NOT NULL DEFAULT 0,
    Embedding            VECTOR(1536) NOT NULL,
    EmbeddingModelId     SMALLINT     NOT NULL,
    SourceTextHash       BINARY(32)   NULL,
    -- standard partitioning column consistent with other *SearchParam tables
);
-- ANN index over Embedding
CREATE VECTOR INDEX ...;
-- Lookup index for upsert/delete by (ResourceTypeId, ResourceSurrogateId, SearchParamId)
```

- The `VECTOR(N)` dimension is fixed at deployment time. Changing it requires a new schema version and full re-embed of existing rows.
- `EmbeddingModelId` is a foreign key to a small reference table `dbo.EmbeddingModel(EmbeddingModelId, ModelName, ModelVersion, Dimension, DistanceMetric, CreatedAt)`. Every vector row is stamped with the model that produced it. When a deployment switches model or version, a new `EmbeddingModelId` row is inserted; queries restrict to the current `EmbeddingModelId` and rows produced by older models are ignored until reindexed. This prevents silent ranking drift when the Foundry deployment is updated.
- `SourceTextHash` (SHA-256 over the embedded input string) is stored to support future no-op-update short-circuits and debugging without persisting the source text itself.
- The table participates in `MergeResources` like every other `*SearchParam` table: rows for a previous version of a resource are deleted in the same transaction in which the new version's rows are inserted. Soft-deleted resources have their vector rows removed.
- Only **current**, non-deleted resource versions have vector rows. Search-on-history is out of scope.

#### Schema version & engine compatibility

- A new schema version is required, following `docs/SchemaVersioning.md` (increment `SchemaVersion`, update `SchemaVersionConstants`, add `Version.diff.sql`, update `LatestSchemaVersion` in the csproj).
- The `VECTOR` type and `CREATE VECTOR INDEX` are presently **Azure SQL Database** features; on-prem SQL Server and older Azure SQL editions do not support them.
- The migration must therefore be **capability-gated**: applying it on an engine that does not advertise vector support fails fast with a clear operator error rather than silently degrading. The feature is opt-in via configuration; servers that do not enable it skip the migration entirely. Open Question §10 covers the precise gating mechanism.

### 6. Search execution (read path)

For a query that targets a vector SP (i.e. the search-parameter cache reports `vector-search-config` on the parameter):

1. Parse the value into a new `VectorSearchExpression { SearchParameterInfo, QueryText, K, MinScore }`. No modifier-name dispatch is involved — the parser's normal type-based routing recognizes the vector SP exactly the way it recognizes a date or reference SP.
2. Server embeds `QueryText` via `IEmbeddingClient` (one Foundry call per search request). Failure here returns `503` with `OperationOutcome` — not an empty result set.
3. The SQL expression visitor emits an ANN candidate query against `VectorSearchParam`, then `INNER JOIN`s the candidate set to the relational predicate tree (filter-after-ANN), ordering by `VECTOR_DISTANCE` and projecting the score.
4. The default planning strategy is **ANN-then-filter with oversampling**: take the top `K * oversamplingFactor` ANN candidates and apply the relational filters, returning up to `_count` results. Oversampling factor is server-configurable; the trade-off (recall vs latency) is called out as Open Question §11.
5. Continuation tokens carry the query embedding (or a server-side handle to it) plus a deterministic tie-breaker (`ResourceSurrogateId`) so that pagination is stable across requests within a session.
6. Authorization / compartment filters compose with the candidate set in exactly the same way as any other search expression — vector search must not rank over resources the caller cannot read, and scores/counts must not be observable for filtered-out resources.

### 7. SearchParameter lifecycle

Adding a vector SP follows the existing atomic SearchParameter CRUD + cache-refresh ownership protocol (ADR 2603), with one addition:

- A newly created vector SP enters status `PendingReindex` and is **not** selectable in queries until vector reindex completes. This avoids returning silently-empty result sets while the vector index is being populated.
- `$reindex` for a vector SP issues one Foundry embedding call per matching resource. The reindex job must:
  - Be checkpointed/restartable using existing reindex job infrastructure.
  - Respect a configurable maximum embeddings-per-second to avoid Foundry throttling.
  - Report cost-relevant telemetry (calls, tokens-in, failures).
  - Be cancellable; partial progress remains valid (rows produced are tagged with the current `EmbeddingModelId`).
- Changing the server-level embedding model invalidates all existing vector rows. The new model gets a new `EmbeddingModelId`; old rows are still present but excluded from search until a full reindex completes. Operators are responsible for triggering the reindex.
- `SearchParamHash` includes the `vector-search-config` extension so that altering it triggers per-resource reindex behavior consistent with the rest of the system.

### 8. Operational controls

- Feature flag at the server level: `Search:VectorSearch:Enabled`. When `false`, the schema migration is skipped, vector SPs are rejected at registration time, and no embedding clients are constructed.
- Kill switch independent of the feature flag: `Search:VectorSearch:Suspended`. When `true`, embedding calls fail fast at read and write paths with `503`, allowing operators to react to a Foundry incident without restarting.
- Circuit breaker around `IEmbeddingClient` to prevent thundering retries during an upstream outage.
- Per-tenant/global rate limiting of embedding calls (write + read).
- Configurable `maxQueryTextLength` to bound query-time embedding cost.
- Telemetry: embedding call count, latency p50/p95/p99, input token count, Foundry status codes, vector rows produced, ANN candidate count, post-filter result count.

### 9. Testing

- Unit tests with a fake `IEmbeddingClient` covering: extraction policies, truncation, hash stability, the new SQL expression visitor, the parser's routing of vector-SP values to `VectorSearchExpression` (and rejection of non-`:missing` modifiers on vector SPs), capability statement projection.
- SQL integration tests against an Azure SQL test database with vector support enabled, gated so they only run in environments where vector is available.
- E2E tests exercising vector search on at least `Observation`, `Condition`, and `DocumentReference` (text portion only), with hybrid filters and `_count` pagination.
- Tests must not hit a real Foundry endpoint in CI; embedding-client tests use a deterministic in-process stub.

### 10. Open Questions

1. **Capability gating mechanism.** Detect Azure SQL vector support via `SERVERPROPERTY`/feature query at startup vs. require an explicit configuration flag, with the failure mode for a misconfigured deployment clearly defined.
2. **Hybrid planner.** Is ANN-then-filter-with-oversampling sufficient for highly selective filters (e.g. `subject=Patient/X`) where the patient may have only a handful of notes? Should we offer a server-side "filter-first then re-rank" path that uses ANN only as a score column over the filtered set?
3. **Multi-tenant cost accounting.** Where in the request pipeline are embedding-call costs attributed to a tenant, and how are quotas expressed and enforced?
4. **Query embedding cache.** Identical vector queries are common; should we cache `QueryText → embedding` in-memory (LRU) with short TTL to reduce per-query Foundry cost? Risk: PHI in cache keys.
5. **Cosmos DB parity.** Cosmos has its own vector index path (DiskANN integration). When and how do we extend this design? Likely a separate ADR.
6. **Model deprecation handling.** When Foundry deprecates the deployed model, what is the operator workflow? Auto-roll-forward to a new `EmbeddingModelId` with a banner indicating reindex is required vs. force-stop until reindex completes.
7. **Attachment text.** A future ADR will cover decoding `Attachment.data`, chunking, and async embedding. v1 does not include it.
8. **Composite & chained vector search.** Useful future work (e.g. find Patients whose Observations contain notes similar to X), but explicitly v2.

## Alternatives Considered

This section captures the major design alternatives evaluated during the investigation. Each is recorded with the trade-offs that drove us toward the chosen approach, so that future ADRs can revisit them as the system or platform evolves.

### A. Vectorization timing (write path)

The choice with the largest operational consequences. Four shapes were on the table.

**A1. Synchronous in-line on Create/Update.** *(Chosen for v1.)* Resource write blocks on the Foundry embedding call before the SQL transaction is committed. Simplest model, strongest read-after-write consistency for vector queries, no separate worker. Pays for it with new external dependency on every write, amplified latency in transaction bundles, and embed-failure → write-failure semantics.

**A2. Async background job via the existing JobQueue.** Resource write commits immediately; a row is enqueued onto `dbo.JobQueue` (the same infrastructure used by import/export/reindex) and a worker drains it, calling Foundry and inserting vector rows. Decouples ingestion latency and Foundry availability from the FHIR write path and lets us batch embedding calls. Trade-offs: resources are temporarily searchable on every parameter *except* the vector SP (eventual consistency window measured in seconds-to-minutes under load), need to surface "vectorization lag" in operator telemetry, and vector queries can return slightly stale result sets. Strong candidate for v2 once cost/latency telemetry from v1 informs the SLA.

**A3. Transactional outbox pattern.** The resource upsert and a `PendingVectorization` marker row are written atomically inside the same SQL transaction. A draining worker reads pending markers, calls Foundry, writes vector rows, and removes the marker — all idempotently. Eliminates the "did the enqueue happen?" failure mode of A2 (no dual-write between SQL and a separate queue), and survives FHIR Server pod restarts cleanly because the marker is durable in the same database that owns the data. Slightly more SQL plumbing than A2 (the marker table, the drain sproc, claim/lease semantics) but objectively the most robust async path. Recommended path for v2 / future work, written up as an Open Question rather than rejected.

**A4. Reindex-only (no automatic vectorization).** Mirrors how brand-new custom search parameters are handled today: rows are populated solely by `$reindex`. Operators add a vector SP, run reindex, and from then on resources are searchable. New writes would *not* be vectorized until the next reindex. Rejected for v1 because it breaks the implicit contract that a search parameter, once installed, applies to all subsequent writes — a contract every other ISearchValue type honors.

The decisive factor for choosing A1 was that it is the simplest behavior to reason about and to *unship* if the feature does not meet expectations: there is no queue to drain, no marker table to clean up, and no eventual-consistency window to explain. Moving from A1 to A2 or A3 in a future revision is a forward-compatible change (existing rows stay valid; only the trigger for producing them changes).

### B. SearchParameter definition mechanism

**B1. `type=special` + Microsoft `vector-search-config` extension.** *(Chosen.)* FHIR-conformant `SearchParameter` resource, rich configuration via extension. External tooling can read the SP even if it doesn't understand the extension.

**B2. `type=string` + an "is-vectorized" extension.** Treat semantic search as a richer string SP with a `:similar` modifier toggle. Rejected because it pollutes the `string` type with two very different storage/index models and confuses tooling that assumes `string` means `:contains`/`:exact`.

**B3. Introduce a non-spec `vector` value for `SearchParameter.type`.** Most expressive, but `SearchParameter.type` is a required-binding FHIR code; emitting an out-of-spec value breaks downstream validators and CapabilityStatement conformance.

**B4. Server-side configuration only — no `SearchParameter` resource.** Operators declare vectorized paths in `appsettings`/a config registry. Simpler to implement but invisible to the FHIR API surface (no CapabilityStatement entry, no `SearchParameter` reads, no atomic CRUD/cache-refresh from ADR 2603). Rejected on principle of "everything searchable should be discoverable".

### C. Query syntax

**C1. Plain parameter — no modifier.** *(Chosen.)* `GET /Observation?clinicalNote=patient short of breath`. The SearchParameter's `vector-search-config` extension is what tells the parser the value is a similarity query, exactly the way `type=date` on `birthdate` causes `?birthdate=2020-01-01` to be parsed as a date predicate. Same URL shape as every other FHIR search, no new `SearchModifierCode` value to introduce, no CapabilityStatement modifier extension needed; FHIR-conformant by construction.

**C2. `:similar` modifier on the SP.** *(Rejected.)* Originally chosen, then dropped. The modifier added no information the parameter definition didn't already carry, and `:similar` is not in the standard `SearchModifierCode` value set — keeping it would have forced us to extend a required-bound code field and advertise the deviation via a capability extension. The only nominal benefit (URL-level "this is a vector query" hint) is illusory, since the parameter name + a `SearchParameter` read already tell the client everything. Standard modifiers like `:missing` are unaffected and continue to work.

**C3. Modifier with required companion query parameters.** `?field:similar=text&_vectorThreshold=…&_vectorK=…`. Same problems as C2, plus mandatory tuning knobs are user-hostile when sensible server defaults work for most queries. v1 keeps `_vectorK` and `_vectorMinScore` as optional reserved parameters (see §2).

**C4. Custom `$vector-search` operation.** `POST /Observation/$vector-search` with a `Parameters` body carrying `query`, `k`, `threshold`, and a nested FHIR search expression for hybrid filters. More expressive (room for multi-field queries, weighted ensembles, future re-ranking parameters) but redefines the query surface and duplicates significant search-parsing infrastructure. Worth revisiting only if we need query shapes that the standard search URL syntax genuinely cannot express.

**C5. Both — modifier *and* `$vector-search` operation.** Tempting "best of both worlds", but doubles the surface area and the test matrix before we have evidence v1 needs it.

### D. Persistence scope

**D1. SQL Server only, with capability gating.** *(Chosen.)* Allows the v1 design to focus on Azure SQL Database's `VECTOR` type and DiskANN ANN index, avoiding parallel design effort for a second store while the FHIR-side abstractions are still settling.

**D2. SQL primary, Cosmos parity noted.** Brief parallel notes for Cosmos's vector index path. Rejected for v1 because writing accurate notes for Cosmos without doing the design work risked encoding wrong assumptions.

**D3. Full design for both persistence stores.** Cleanest end state but roughly doubles design and implementation cost up front.

**D4. Persistence-agnostic abstraction layer with one concrete impl.** Mirrors how existing ISearchValue types layer abstraction over storage. Considered, but the SQL-side details (VECTOR column dimension, ANN index shape, oversampling planner) leak through the abstraction in ways that would force a redesign once Cosmos is added. Better to ship one store, learn, then abstract.

### E. Embedding provider integration

**E1. Foundry only, managed identity, fixed model & dimension.** *(Chosen.)* Minimum surface area, one auth model, one set of operational concerns.

**E2. Foundry with per-SP model/dimension override.** Rejected because mixed dimensions in a single SQL `VECTOR(N)` column are not representable; supporting it would require either per-SP tables or a sparse/variable vector type neither of which Azure SQL exposes.

**E3. Abstract `IEmbeddingProvider` with Foundry as the default.** A thin `IEmbeddingClient` interface is retained for testability (so unit tests can use a fake), but full provider pluggability (AOAI direct, self-hosted) is intentionally out of scope until a second provider is actually required.

**E4. Foundry plus a content-hash embedding cache.** Skip Foundry calls when an identical input text was embedded recently. Real cost-savings potential, but the cache becomes a PHI-bearing data store with retention/eviction policy and is more sensitive than the SQL search rows themselves. Deferred to Open Question §10.4.

## Consequences

### Benefits
- Enables semantic / paraphrase-tolerant search over clinically meaningful text fields without bolting on an external search engine, preserving the FHIR Server as the single query surface and authorization boundary.
- Reuses existing infrastructure: SearchParameter resource shape, search-parameter cache (ADR 2603), MergeResources upsert path, `$reindex` job machinery, schema versioning, ISearchValue/ISearchValueVisitor.
- ANN lives next to relational indexes in the same SQL database, so hybrid filter+similarity queries execute in one engine with one transaction boundary and one authorization model.
- Managed identity removes static credentials from operator burden.

### Adverse effects
- **New external runtime dependency on every write.** Foundry availability now affects ingestion latency and success rate. Mitigated by retries, circuit breaker, kill switch, and the per-bundle "embed before transaction" sequencing, but not eliminated.
- **PHI flows to the embedding endpoint.** Clinical text leaves the FHIR Server process boundary. Operators must explicitly opt the deployment in, align Foundry region with FHIR Server region, prefer private endpoints, disable Foundry content/prompt logging, and document the data flow in their compliance posture (BAA / HITRUST / data-residency). A security and compliance review is required before this feature moves out of `Proposals/`.
- **Embedding cost scales with write volume and reindex.** Operators need cost visibility (telemetry §8) and rate controls. A surprise `$reindex` of a busy `Observation.note` can be expensive.
- **Engine compatibility narrows.** v1 works only on Azure SQL Database editions that expose vector type + ANN index. On-prem SQL Server and older Azure SQL editions cannot enable the feature; the migration must refuse to apply rather than degrade.
- **Preview surface area.** Azure SQL vector type is preview at the time of writing; syntax and operator names may change. The implementation should isolate vector-DDL/DML behind a thin SQL layer to minimize churn when the preview GAs.
- **Limited conformance footprint.** The `SearchParameter` resource is conformant (`type=special` + a Microsoft extension, which is exactly what FHIR extensions are designed for). The URL shape of a query is conformant too — no non-standard modifier is introduced. The only Microsoft-specific surface visible to clients is the pair of optional reserved query parameters `_vectorK` / `_vectorMinScore`; standard FHIR tooling can issue a vector query without them and will simply receive server-defaulted behavior. The `vector-search-config` extension on the `SearchParameter` resource is the FHIR-native way for clients to discover that the parameter has similarity semantics.

### Neutral effects
- Adds a new schema version and a new search-value type, increasing the surface area touched by future search-pipeline refactors.
- The `EmbeddingModel` reference table and `EmbeddingModelId` stamp will be permanent even if the team later collapses to a single model.

## References
- ADR 2603 — Non-Spec Default Search Parameters
- ADR 2603 — Atomic SearchParameter CRUD and Cache-Refresh Ownership
- ADR 2603 — Load-Independent Search Parameter Cache Sync
- ADR 2510 — Meta History (resource versioning semantics that constrain history search)
- `docs/SchemaVersioning.md` — SQL schema version & migration rules
- FHIR R4 Search — https://hl7.org/fhir/R4/search.html
- FHIR `SearchModifierCode` value set — https://hl7.org/fhir/R4/valueset-search-modifier-code.html
- Azure SQL Database vector data type & DiskANN index (Microsoft Learn)
- Azure AI Foundry embeddings (Microsoft Learn)
