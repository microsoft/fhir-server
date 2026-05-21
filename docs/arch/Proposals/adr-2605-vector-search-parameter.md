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

We will introduce a **Vector Search Parameter** as a new ISearchValue kind, persisted in a new SQL table backed by an Azure SQL vector index, with embeddings produced synchronously by Azure AI Foundry over managed identity at resource write time. Clients invoke it via a `:similar` modifier on the parameter name.

### 1. Search parameter definition

A vector SP is declared as a normal FHIR `SearchParameter` resource so that it appears in `CapabilityStatement.rest.resource.searchParam` and can be loaded by the existing search-parameter cache machinery (see ADR 2603 *Atomic SearchParameter CRUD and Cache-Refresh Ownership* and ADR 2603 *Non-Spec Default Search Parameters*).

- `SearchParameter.type` = `special` — this is the closest FHIR-conformant value for a parameter whose semantics are not covered by the standard types.
- `SearchParameter.expression` = standard FHIRPath expression identifying the source text node(s) (e.g. `Observation.note.text`).
- A Microsoft extension `http://microsoft.com/fhir/StructureDefinition/vector-search-config` on the `SearchParameter` resource carries vector-specific configuration:
  - `kind` — discriminator, fixed to `vector` for v1.
  - `extractionPolicy` — `firstValue` | `concatenate` | `perValueRow` (see §4).
  - `maxInputTokens` — truncation budget per embedding call.
  - `searchableOnHistory` — must be `false` in v1; reserved for future use.

The embedding model name, model version/deployment fingerprint, vector dimension, and distance metric are **not** on the `SearchParameter` — they are server-level configuration. This avoids the situation where two vector SPs declare incompatible dimensions and the SQL schema cannot represent both.

### 2. Query syntax

```
GET /Observation?clinicalNote:similar=patient short of breath at rest&subject=Patient/123&_count=20
```

- `:similar` is a Microsoft extension to the FHIR `SearchModifierCode` value set. It is **not** in the standard value set, so the parser will accept it only for search parameters whose definition has the `vector-search-config` extension; using `:similar` on any other parameter returns `400 invalid-modifier`.
- Combinable with any other search parameter. The combined predicate is a **hybrid** query: ANN over the vector index intersected with the relational predicates produced by the rest of the expression tree.
- Optional reserved companion parameters (under `_` prefix to avoid colliding with future spec parameters):
  - `_vectorK` — number of nearest neighbors to consider before applying filters. Default server-side, e.g. 200.
  - `_vectorMinScore` — minimum normalized similarity score (0..1) below which results are dropped.
- Result scoring: results carry `Bundle.entry.search.score` populated as a cosine-similarity-derived 0..1 score (higher = more similar). The `Bundle.entry.search.mode` remains `match`.
- Capability advertising: `CapabilityStatement.rest.resource.searchParam` will carry a Microsoft extension `vector-search-supported` to make `:similar` discoverable by tooling without claiming `:similar` is a base-spec modifier.

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

For a query containing a `:similar` modifier:

1. Parse `:similar` into a new `VectorSearchExpression { SearchParameterInfo, QueryText, K, MinScore }`.
2. Server embeds `QueryText` via `IEmbeddingClient` (one Foundry call per search request). Failure here returns `503` with `OperationOutcome` — not an empty result set.
3. The SQL expression visitor emits an ANN candidate query against `VectorSearchParam`, then `INNER JOIN`s the candidate set to the relational predicate tree (filter-after-ANN), ordering by `VECTOR_DISTANCE` and projecting the score.
4. The default planning strategy is **ANN-then-filter with oversampling**: take the top `K * oversamplingFactor` ANN candidates and apply the relational filters, returning up to `_count` results. Oversampling factor is server-configurable; the trade-off (recall vs latency) is called out as Open Question §11.
5. Continuation tokens carry the query embedding (or a server-side handle to it) plus a deterministic tie-breaker (`ResourceSurrogateId`) so that pagination is stable across requests within a session.
6. Authorization / compartment filters compose with the candidate set in exactly the same way as any other search expression — vector search must not rank over resources the caller cannot read, and scores/counts must not be observable for filtered-out resources.

### 7. SearchParameter lifecycle

Adding a vector SP follows the existing atomic SearchParameter CRUD + cache-refresh ownership protocol (ADR 2603), with one addition:

- A newly created vector SP enters status `PendingReindex` and is **not** selectable by `:similar` queries until vector reindex completes. This avoids returning silently-empty result sets while the vector index is being populated.
- `$reindex` for a vector SP issues one Foundry embedding call per matching resource. The reindex job must:
  - Be checkpointed/restartable using existing reindex job infrastructure.
  - Respect a configurable maximum embeddings-per-second to avoid Foundry throttling.
  - Report cost-relevant telemetry (calls, tokens-in, failures).
  - Be cancellable; partial progress remains valid (rows produced are tagged with the current `EmbeddingModelId`).
- Changing the server-level embedding model invalidates all existing vector rows. The new model gets a new `EmbeddingModelId`; old rows are still present but excluded from search until a full reindex completes. Operators are responsible for triggering the reindex.
- `SearchParamHash` includes the `vector-search-config` extension so that altering it triggers per-resource reindex behavior consistent with the rest of the system.

### 8. Operational controls

- Feature flag at the server level: `Search:VectorSearch:Enabled`. When `false`, the schema migration is skipped, `:similar` is rejected at parse time, and no embedding clients are constructed.
- Kill switch independent of the feature flag: `Search:VectorSearch:Suspended`. When `true`, embedding calls fail fast at read and write paths with `503`, allowing operators to react to a Foundry incident without restarting.
- Circuit breaker around `IEmbeddingClient` to prevent thundering retries during an upstream outage.
- Per-tenant/global rate limiting of embedding calls (write + read).
- Configurable `maxQueryTextLength` to bound query-time embedding cost.
- Telemetry: embedding call count, latency p50/p95/p99, input token count, Foundry status codes, vector rows produced, ANN candidate count, post-filter result count.

### 9. Testing

- Unit tests with a fake `IEmbeddingClient` covering: extraction policies, truncation, hash stability, the new SQL expression visitor, modifier parser acceptance/rejection rules, capability statement projection.
- SQL integration tests against an Azure SQL test database with vector support enabled, gated so they only run in environments where vector is available.
- E2E tests exercising the `:similar` modifier on at least `Observation`, `Condition`, and `DocumentReference` (text portion only), with hybrid filters and `_count` pagination.
- Tests must not hit a real Foundry endpoint in CI; embedding-client tests use a deterministic in-process stub.

### 10. Open Questions

1. **Capability gating mechanism.** Detect Azure SQL vector support via `SERVERPROPERTY`/feature query at startup vs. require an explicit configuration flag, with the failure mode for a misconfigured deployment clearly defined.
2. **Hybrid planner.** Is ANN-then-filter-with-oversampling sufficient for highly selective filters (e.g. `subject=Patient/X`) where the patient may have only a handful of notes? Should we offer a server-side "filter-first then re-rank" path that uses ANN only as a score column over the filtered set?
3. **Multi-tenant cost accounting.** Where in the request pipeline are embedding-call costs attributed to a tenant, and how are quotas expressed and enforced?
4. **Query embedding cache.** Identical `:similar` queries are common; should we cache `QueryText → embedding` in-memory (LRU) with short TTL to reduce per-query Foundry cost? Risk: PHI in cache keys.
5. **Cosmos DB parity.** Cosmos has its own vector index path (DiskANN integration). When and how do we extend this design? Likely a separate ADR.
6. **Model deprecation handling.** When Foundry deprecates the deployed model, what is the operator workflow? Auto-roll-forward to a new `EmbeddingModelId` with a banner indicating reindex is required vs. force-stop until reindex completes.
7. **Attachment text.** A future ADR will cover decoding `Attachment.data`, chunking, and async embedding. v1 does not include it.
8. **Composite & chained vector search.** Useful future work (e.g. find Patients whose Observations contain notes similar to X), but explicitly v2.

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
- **Conformance asymmetry.** The `SearchParameter` resource itself remains conformant (`type=special`), but the `:similar` modifier and `_vectorK` / `_vectorMinScore` reserved parameters are Microsoft extensions. Standard FHIR tooling will not understand them; clients must read the capability extension to discover support.

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
