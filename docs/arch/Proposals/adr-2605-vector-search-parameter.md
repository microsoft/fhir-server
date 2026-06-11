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

We will introduce a **Vector Search Parameter** as a new ISearchValue kind, persisted in a new SQL table backed by an Azure SQL vector index, with embeddings produced by Azure AI Foundry over managed identity. Embedding persistence is **operator-selectable between two modes**: **synchronous** (the embedding call completes before the resource's SQL write transaction commits — strongest consistency, write latency coupled to Foundry) and **asynchronous** (a watchdog tracks a watermark over the existing `dbo.Transactions` log and enqueues embedding-generation jobs onto the existing `dbo.JobQueue`, producing vector rows shortly after commit — write path fully decoupled from Foundry, eventually-consistent vector search). A per-request `Prefer: embedding-persistence=synchronous` preference (RFC 7240) lets a client request best-effort inline embedding for an individual write when the server runs in asynchronous mode, with `Preference-Applied` signaling whether it was honored. A vector SP is declared as a FHIR `SearchParameter` with `type=special` and a Microsoft `vector-search-config` extension; clients query it with the standard `?paramName=text` URL shape, and the parser uses the parameter's declared shape — not a modifier — to interpret the value as a similarity query.

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

Embedding persistence is governed by a server-level setting:

```
Search:VectorSearch:EmbeddingPersistenceMode = Synchronous | Asynchronous   (default: Synchronous)
```

Both modes share the same extraction, policy, and persistence machinery; they differ only in *when* the Foundry call happens relative to the resource's SQL transaction. This is a deliberate departure from the implicit contract that every `*SearchParam` table row commits atomically with its resource — asynchronous mode breaks that contract for the vector table only, and the consequences are spelled out in §3.3.

#### 3.1 Synchronous mode

Resource Create/Update embeds the extracted text before the SQL write transaction completes:

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
- Read-after-write consistency: a successful write is immediately findable via its vector SP.

#### 3.2 Asynchronous mode (transaction-log watermark + JobQueue)

Resource Create/Update commits immediately and the write path does **nothing vector-specific**. Vectorization is driven entirely from the existing durable infrastructure:

```
VectorizationWatchdog (Watchdog<T> lease — single active enqueuer; pattern: DefragWatchdog)
  └─ read watermark 'VectorizationWatchdog.LastProcessedTransactionId' from dbo.Parameters
  └─ visibility = MergeResourcesGetTransactionVisibilityAsync()        ← in-flight-safe upper bound
  └─ enumerate committed transactions in (watermark, visibility]       ← GetTransactionsAsync pattern
  └─ enqueue EmbeddingGeneration jobs (QueueType = 7) onto dbo.JobQueue,
     range-batched as {ResourceTypeId, StartSurrogateId, EndSurrogateId}
     for resource types that have ≥1 enabled vector SP
  └─ advance watermark

EmbeddingGenerationJob (IJob — existing JobHosting workers, parallel across instances)
  └─ load current, non-deleted resources in the surrogate-id range
  └─ re-extract text via the same ISearchIndexer as the write path, apply policy (§4)
  └─ skip resources whose stored (SourceTextHash, EmbeddingModelId) already match   ← idempotency
  └─ call IEmbeddingClient.EmbedAsync (batched)
  └─ merge VectorSearchParam rows (delete-then-insert per resource, transactional)
```

Key properties:
- **No new durable state beyond one watermark row.** The `dbo.Transactions` log is already a durable, ordered record of every committed write, and `MergeResourcesGetTransactionVisibilityAsync` already solves the in-flight-transaction race (surrogate IDs that commit out of order). A dedicated outbox/marker table would duplicate exactly this; see alternative A3. The watermark lives in `dbo.Parameters`, the same mechanism `InvisibleHistoryCleanupWatchdog` uses for `LastCleanedUpTransactionId`.
- **No dual-write.** The write path does not enqueue anything; jobs are derived after the fact from the transaction log. A watchdog crash between enqueue and watermark advance merely re-enqueues a range — safe, because jobs are idempotent via the `SourceTextHash` + `EmbeddingModelId` short-circuit.
- **No PHI in the queue.** Job definitions carry only `{ResourceTypeId, StartSurrogateId, EndSurrogateId}`. Workers re-extract source text from the resource rows, which live in the same database under the same controls.
- **Retry, poison handling, parallelism, cancellation, and monitoring are inherited**, not built: JobQueue dequeue/heartbeat semantics retry crashed jobs; a job that exhausts retries lands in `Failed` status visible through existing job APIs; multiple workers across instances drain in parallel (embedding throughput is rate-limited at the `IEmbeddingClient`); and queue depth/stale-job telemetry comes from the existing job-monitoring watchdog (`Jobs.QueueDepth`).
- **Superseded versions cost nothing.** A resource updated again before its job runs simply fails the "current version" check or the hash check at job time; the newer transaction produces its own job.
- Failure mode for the FHIR write: **none introduced.** Foundry being down does not fail or slow resource ingestion; the watchdog keeps advancing and failed jobs are retried/parked by the queue.

#### 3.3 Consistency semantics in asynchronous mode

- A newly written resource is searchable on every parameter *except* its vector SPs until its job completes (expected seconds; minutes under backlog). Operators get a `VectorizationLag` metric (watermark age: visibility minus watermark, plus oldest active job age) alongside the existing `Jobs.QueueDepth` metric for the embedding queue type.
- `:missing=true` on a vector SP will match a resource whose vectorization is still pending — indistinguishable from "has no vectorizable text". Documented behavior; clients needing strict semantics should run synchronous mode or send the per-request preference (§3.4).
- Vector query results may omit very recent writes. Scores and ordering for already-vectorized resources are unaffected.
- Switching modes is safe at any time: the watchdog runs whenever the feature is enabled, regardless of mode, and always continues from its watermark — so backlog accumulated in asynchronous mode keeps draining after a switch to synchronous. In synchronous mode the enqueued jobs make no embedding calls (the hash short-circuit skips already-embedded resources), so the steady-state cost is SQL reads over the write delta. Note the watermark only moves forward: a job that ultimately *fails* leaves its range behind the watermark, which is why failed-job recovery is called out in Open Question §10.9.

#### 3.4 Per-request synchronous preference (`Prefer` header)

In asynchronous mode, a client that wants read-after-write consistency for a specific write requests it through the standard HTTP `Prefer` mechanism (RFC 7240), which FHIR already uses (`return=`, `handling=`, `respond-async`):

```
Prefer: embedding-persistence=synchronous
```

Semantics are **best-effort with an explicit honored/not-honored signal**, which is exactly what RFC 7240 preferences are for:

- The server attempts inline embedding before the commit, under a deliberately tight budget (single attempt, short timeout — not the full retry/backoff policy of synchronous mode, so a degraded Foundry cannot stall interactive writes).
- **Success:** the response carries `Preference-Applied: embedding-persistence=synchronous`, and the resource is immediately findable via its vector SPs. The later watchdog-enqueued job no-ops via the hash short-circuit — no duplicate embedding cost.
- **Embedding failure or timeout:** the write **still commits** — no error — and the response simply omits `Preference-Applied`. The transaction-log watermark pipeline vectorizes the resource shortly after, exactly as if the preference had not been sent. The client detects the miss from the absent header and can poll or retry the query. This is deliberately *not* an error: failing a write whose durable outcome would be identical either way punishes the client for an optimization hint, and RFC 7240 preferences must not change success semantics. A client that genuinely cannot tolerate eventual consistency belongs on a synchronous-mode deployment, where embed-failure → `503` is the operator's chosen contract (§3.1).
- For a transaction bundle, the preference applies to the whole bundle: all extracted texts are embedded in one batched call (§3.5); `Preference-Applied` is emitted only if the entire bundle embedded inline.
- In synchronous mode the preference is already satisfied (`Preference-Applied` is echoed). The value `embedding-persistence=asynchronous` is never honored in synchronous mode — per RFC 7240 it is silently ignored (no `Preference-Applied`): clients must not be able to downgrade an operator's consistency choice.

#### 3.5 Batched embedding calls

The Foundry embeddings endpoint accepts an array of inputs per request — up to 2,048 inputs and 300,000 aggregate tokens per call (8,192 tokens per input) for the text-embedding-3 family. `IEmbeddingClient` therefore takes a list of inputs and chunks by both array-size and aggregate-token limits internally. Consequences:

- A transaction bundle of N resources × M vector SPs costs **one** embedding HTTP call in synchronous mode (until limits force chunking), not N×M.
- The asynchronous job batches an entire surrogate-id range into the minimum number of calls.
- The per-input token truncation budget (`maxInputTokens`, §1) must also respect the 8,192 per-input ceiling.

#### 3.6 Shared properties (both modes)

- Authentication: `DefaultAzureCredential` chain with managed identity as the intended production credential; no static keys.
- The embedding client is abstracted as `IEmbeddingClient` with a single production implementation `AzureAIFoundryEmbeddingClient`. The interface exists for testability, not provider pluggability.

#### Bulk import (`$import`)

`$import` does **not** use the standard MediatR upsert pipeline. v1 behavior is mode-dependent:
- **Synchronous mode:** if any vector SP is registered for an imported resource type, `$import` records a per-resource-type warning in the import outcome and **skips** vector row generation (the import path never calls the enricher). A follow-up `$reindex` populates vector rows. Rationale: synchronous embedding would dominate the cost/latency of bulk import for marginal benefit; import first, then reindex deliberately.
- **Asynchronous mode:** imported merges land in the `dbo.Transactions` log like any other write, so the watchdog vectorizes imported resources automatically on its normal cadence. There is no way (and no attempt) to distinguish import-originated transactions. This means **a large `$import` in async mode automatically incurs the full embedding cost of the imported corpus** — throttled by the embedding rate limit and visible in cost telemetry, but not gated by an explicit operator action. The import outcome warning notes this. Whether an operator knob to exclude imports from async vectorization is needed is Open Question §10.11.

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

#### Asynchronous-mode state (no new tables)

Asynchronous mode deliberately introduces **no new durable queue table**:

- The work source is the existing `dbo.Transactions` log (every committed merge already records a transaction), read through `MergeResourcesGetTransactionVisibilityAsync` so in-flight transactions are never skipped.
- Progress is a single watermark row in `dbo.Parameters` (`VectorizationWatchdog.LastProcessedTransactionId`), the same pattern `InvisibleHistoryCleanupWatchdog` uses.
- In-flight work units are ordinary `dbo.JobQueue` rows with a new `QueueType = EmbeddingGeneration (7)`; definitions carry `{ResourceTypeId, StartSurrogateId, EndSurrogateId}` only — never source text or embeddings.

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
- Persistence mode: `Search:VectorSearch:EmbeddingPersistenceMode` = `Synchronous` (default) | `Asynchronous`. See §3. Async-specific knobs: job range batch size (`JobBatchSize`), watchdog period/lease (via the standard watchdog `Parameters` table rows); retry/poison behavior is the JobQueue's own dequeue-count semantics.
- Per-request preference `Prefer: embedding-persistence=synchronous` with `Preference-Applied` response signal (§3.4).
- Kill switch independent of the feature flag: `Search:VectorSearch:Suspended`. When `true`, embedding calls fail fast at read and write paths with `503`, allowing operators to react to a Foundry incident without restarting.
- Circuit breaker around `IEmbeddingClient` to prevent thundering retries during an upstream outage.
- Per-tenant/global rate limiting of embedding calls (write + read).
- Configurable `maxQueryTextLength` to bound query-time embedding cost.
- Telemetry: embedding call count, latency p50/p95/p99, input token count, Foundry status codes, vector rows produced, ANN candidate count, post-filter result count. Async mode additionally: `VectorizationLag` (visibility minus watermark age), embedding-queue depth and failed-job count (both via the existing job-monitoring watchdog metrics for the new queue type).

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
9. **Failed-job recovery workflow.** In async mode, embedding jobs that exhaust JobQueue retries land in `Failed` status, visible through existing job APIs and queue metrics. Because the watchdog sweep is self-healing (§3.3), a failed range is re-covered on a later pass only if the watermark has not advanced past it — define whether failed ranges are automatically re-enqueued on a long cadence, or recovery is `$reindex`/operator-driven. Needs a decision before GA.
10. **Per-tenant mode override.** `EmbeddingPersistenceMode` is server-global in v1. Multi-tenant deployments may want per-tenant sync/async; deferred until there is demand.
11. **Async vectorization of `$import`.** In async mode, imported resources are vectorized automatically via the transaction-log watermark (§3 bulk import) — the embedding cost of a large import is incurred without a distinct opt-in. Decide before GA whether this needs an operator gate (e.g. pause the watchdog during import windows, or a surrogate-id exclusion range) or whether rate limiting + cost telemetry is sufficient.

## Alternatives Considered

This section captures the major design alternatives evaluated during the investigation. Each is recorded with the trade-offs that drove us toward the chosen approach, so that future ADRs can revisit them as the system or platform evolves.

### A. Vectorization timing (write path)

The choice with the largest operational consequences. Four shapes were on the table.

**A1. Synchronous in-line on Create/Update.** *(Shipped in v1 as the `Synchronous` mode, and the default.)* Resource write blocks on the Foundry embedding call before the SQL transaction is committed. Simplest model, strongest read-after-write consistency for vector queries, no separate worker. Pays for it with new external dependency on every write, amplified latency in transaction bundles, and embed-failure → write-failure semantics.

**A2. Async via transaction-log watermark + the existing JobQueue.** *(Shipped in v1 as the `Asynchronous` mode.)* A lease-based watchdog tracks a watermark over `dbo.Transactions` (a `dbo.Parameters` row, the `InvisibleHistoryCleanupWatchdog` pattern) and enqueues range-batched `EmbeddingGeneration` jobs onto `dbo.JobQueue`; existing JobHosting workers process them idempotently. An earlier draft rejected JobQueue on the grounds that per-resource-write enqueueing from the write path would turn a job-coordination table into a high-volume event queue and introduce a dual-write ("did the enqueue happen?") failure mode. Both objections dissolve once enqueueing is (a) derived from the transaction log after commit rather than performed by the write path, and (b) batched into surrogate-id ranges — the same granularity Defrag and Export already use. What JobQueue then provides for free is substantial: dequeue/heartbeat retry, poison handling via `Failed` status, parallel workers across instances, cancellation, and existing queue-depth/stale-job monitoring.

**A3. Transactional outbox pattern.** A marker row written atomically with the resource in MergeResources, drained by a dedicated worker against a new `dbo.VectorSearchOutbox` table. An earlier draft of this ADR chose this for the async mode. Rejected on closer inspection because it duplicates durable state the system already maintains: the `dbo.Transactions` log *is* an ordered, durable record of every committed write, and `MergeResourcesGetTransactionVisibilityAsync` already solves the in-flight-transaction visibility race the outbox would otherwise re-solve. The outbox also required touching MergeResources (the hottest sproc in the system) and re-implementing claim/lease/retry/poison semantics that JobQueue already has. A2-with-watermark strictly dominates it here. The outbox pattern remains the right tool where no equivalent transaction log exists — that is not this codebase.

**A4. Reindex-only (no automatic vectorization).** Mirrors how brand-new custom search parameters are handled today: rows are populated solely by `$reindex`. Operators add a vector SP, run reindex, and from then on resources are searchable. New writes would *not* be vectorized until the next reindex. Rejected for v1 because it breaks the implicit contract that a search parameter, once installed, applies to all subsequent writes — a contract every other ISearchValue type honors.

**Why both A1 and A2 instead of one.** An earlier draft of this ADR chose A1 alone, on the grounds that it was the simplest behavior to reason about and to unship. That reasoning underweighted the operational reality: synchronous mode makes Foundry availability a hard dependency of resource ingestion, which is unacceptable for high-write-volume deployments and for operators whose ingestion SLA is stricter than their search-freshness SLA. Conversely, async-only would force every deployment to accept eventual consistency even when their write volume makes synchronous embedding trivially cheap. The two modes share all machinery except the trigger, and the async trigger is assembled almost entirely from existing parts (watchdog base class, Parameters watermark, JobQueue, transaction visibility), so the marginal cost of shipping both is small relative to the cost of retrofitting either later. Both modes produce identical `VectorSearchParam` rows, so switching modes requires no data migration; the per-request `Prefer: embedding-persistence=synchronous` preference (§3.4) additionally lets individual writes opt up to synchronous consistency — best-effort, with `Preference-Applied` as the honored signal — without a mode change.

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
- **New external runtime dependency on every write (synchronous mode).** Foundry availability now affects ingestion latency and success rate. Mitigated by retries, circuit breaker, kill switch, and the per-bundle "embed before transaction" sequencing — or eliminated entirely by running asynchronous mode.
- **Asynchronous mode breaks the atomic-indexing invariant.** Today, every `*SearchParam` row commits in the same transaction as its resource; async mode is the first place in the codebase where a search index trails its resource. Code that assumes "resource committed ⟹ fully indexed" (reindex up-to-date checks via `SearchParamHash`, `:missing` semantics, test helpers) must treat the vector table as the documented exception. This is a deliberate, contained breakage — confined to one table and one mode — but it is a precedent, and §3.3 exists so it stays explicit rather than folklore.
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
