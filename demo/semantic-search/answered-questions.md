# Answered Questions

## How does the ingest write path know which text fields from resources to embed?

The write path is metadata-driven. It does not scan every string in a resource, ask the embedding model to choose fields, or use a resource-type switch in the indexer. An active, supported, and searchable FHIR `SearchParameter` defines the text source and becomes eligible for vector indexing through the server registry.

Each vector SearchParameter supplies the following contract:

| Setting | Purpose |
|---|---|
| `url` | Gives the definition a canonical identity used by the registry and persisted vector rows. |
| `base` | Declares which FHIR resource types the definition applies to. |
| `expression` | Selects values from the resource using FHIRPath. |
| `type` | Must be `special` for a vector SearchParameter. |
| Vector configuration extension | Defines how selected values become text, how multiple values are handled, and the maximum input size. |
| Operational status | Must be supported and searchable before the definition is used for vector indexing or querying. |

### Write flow

1. The normal FHIR search indexer evaluates all applicable SearchParameter expressions when it creates the `ResourceWrapper`. The vector SearchParameter expression therefore produces ordinary string-valued `SearchIndexEntry` instances alongside the other extracted search values.
2. Before SQL persists a changed resource, `SqlServerFhirDataStore` calls `VectorSearchIndexer` for the resources in that write batch.
3. `VectorSearchParameterResolver` reads the server SearchParameter registry and returns active, supported, searchable vector definitions applicable to the resource type being written.
4. `VectorSearchIndexer` finds the previously extracted string values by exact SearchParameter canonical URL. This connects the FHIRPath expression to the vector indexing step without reevaluating or duplicating field-selection logic.
5. The SearchParameter's `sourceStrategy` turns each extracted value into source text:
   * `directText` uses the extracted string itself.
   * `localBinaryReference` treats the value as a local `Binary/{id}` reference, loads that Binary, and extracts UTF-8 `text/plain` or page-scoped text from `application/pdf` content in `Binary.data`.
6. The `extractionPolicy` controls multiple values. `firstValue` keeps the first value, `concatenate` joins values from the same source, and `perValueRow` keeps each value as a separate source.
7. The selected text is chunked, sent to the embedding endpoint, and persisted with its SearchParameter canonical and source provenance.

### Current R4 mappings

The built-in Microsoft definitions currently demonstrate three ways to select text:

| Resource | FHIRPath expression | Source strategy | Text embedded |
|---|---|---|---|
| `Observation` | `Observation.note.text` | `directText` | Each selected note text value. |
| `DiagnosticReport` | `DiagnosticReport.conclusion` | `directText` | The report conclusion. |
| `DocumentReference` | `DocumentReference.content.attachment.url.toString()` | `localBinaryReference` | UTF-8 plain text decoded from the referenced local `Binary.data`. |

These mappings are definitions, not indexer code. Supporting another resource or another field means defining an active vector SearchParameter with the appropriate `base`, FHIRPath `expression`, and vector extension, registering it with the FHIR server, and completing the normal lifecycle until it is searchable.

New or changed resources use that definition on the normal write path. Existing resources can use `$reindex` to rebuild both standard and vector search indices.

## What is the current status of synchronous versus asynchronous indexing?

The implementation is synchronous today. When vector search is enabled and a resource write actually changes the stored resource, the SQL write path calls `VectorSearchIndexer` before the resource batch is persisted. The indexer resolves source text, chunks it, calls the embedding endpoint, and attaches vector index rows to the `ResourceWrapper`. The SQL merge then writes both the normal search parameter rows and the vector rows.

Current behavior:

| Area | Current status |
|---|---|
| Write indexing mode | `Synchronous` only. |
| Configuration validation | Rejects any indexing mode other than `VectorSearchIndexingMode.Synchronous`. |
| Read-after-write behavior | A successful changed write should be immediately searchable by vector search. |
| Write latency | Includes text resolution, chunking, embedding endpoint latency, and SQL vector persistence. |
| Failure behavior | If embedding generation fails during the synchronous path, the write cannot safely produce a complete semantic index for that resource. |

The planned asynchronous mode is intended for write-heavy deployments. In that mode, the resource write would commit first and enqueue semantic indexing work for a background processor. That would reduce write latency but introduce eventual consistency: a newly written resource could be visible to normal FHIR search before its semantic vector rows exist.

The planned async design needs these pieces before it is production-ready:

| Needed piece | Reason |
|---|---|
| Durable queue/job record | Indexing work must survive process restarts. |
| Resource version capture | The background worker must embed the version that triggered the job, not accidentally index stale or newer content without checking. |
| Idempotent vector writes | Retries must safely replace or skip already indexed rows. |
| Retry and poison handling | Transient embedding failures should retry without blocking ordinary FHIR writes forever. |
| Visibility/status signal | Operators need to know whether vector indexing is caught up or lagging. |

## How does dependency injection work now?

Semantic search services are registered only when both conditions are true:

1. The runtime data store is SQL Server (`AzureHealthDataServicesRuntimeConfiguration`).
2. `FhirServer:CoreFeatures:VectorSearch:Enabled` is `true`.

If either condition is false, `Startup.AddSemanticSearch` returns without registering semantic services. In that state the optional `IVectorSearchIndexer` dependency in `SqlServerFhirDataStore` is `null`, so write-time vector indexing is skipped.

When enabled, the main registrations are:

| Service | Lifetime | Implementation / purpose |
|---|---|---|
| `ITextChunker` | Singleton | `TextChunker`, splits source text into passages. |
| `IVectorSearchParameterResolver` | Singleton | Discovers eligible vector SearchParameters from the server registry. |
| `IVectorTextSourceResolver` | Scoped | Resolves `directText` and local Binary text sources. |
| `IEmbeddingClient` | Scoped | `AzureFoundryEmbeddingClient`, calls the configured Azure OpenAI / Foundry embedding deployment. |
| `IVectorStore` | Scoped | `SqlVectorStore`, legacy/prototype vector store path. |
| `IEmbeddingModelRegistry` | Singleton | `SqlEmbeddingModelRegistry`, resolves and caches `EmbeddingModelId`. |
| `IVectorSearchIndexer` | Scoped | `VectorSearchIndexer`, write-time vector indexer. |
| `IVectorSearchQueryProcessor` | Scoped | `VectorSearchQueryProcessor`, query-time embedding preparation. |
| `IDocumentReferenceSemanticSearch` | Scoped | `SqlDocumentReferenceSemanticSearch`, patient-level semantic operation backing service. |

Authentication for the embedding client uses `TokenCredential`. Local development uses developer sign-in such as `az login`; production should use managed identity. No embedding API key is stored in configuration.

## How does model versioning work?

The SQL schema has an `EmbeddingModel` registry table. The active configuration supplies `ModelName`, `ModelVersion`, `Dimensions`, and `DistanceMetric`. At runtime, `SqlEmbeddingModelRegistry` looks up a row by `(ModelName, ModelVersion)` under a serializable transaction. If no row exists, it inserts one. If a row exists but the configured dimension or distance metric differs, startup/query-time resolution fails instead of silently mixing incompatible vectors.

Each row in `VectorSearchParam` stores `EmbeddingModelId`. Query preparation also resolves the active `EmbeddingModelId`, and SQL filters vector candidates to that ID before ranking. This prevents comparing vectors from different model versions.

Important consequence: changing `ModelName` or `ModelVersion` creates or selects a different model registry row. Existing resources do not automatically get new vectors for that model. Run `$reindex` or perform a real resource rewrite to generate vectors under the new model ID; until then, queries can return fewer or no semantic results.

There are two versioning concepts in play:

| Versioning area | What it means |
|---|---|
| Embedding model version | Controls vector compatibility through `EmbeddingModelId`. |
| FHIR resource version | Captured in semantic evidence when available, so a returned passage can point back to the source resource version. |

## What normalization happens?

There are three separate normalization questions.

| Area | Current behavior |
|---|---|
| Query text / source text | The text is not lowercased, stemmed, keyword-normalized, or clinically rewritten before embedding. The embedding model receives the selected source text or query text. |
| Embedding vectors | The production Azure embedding client returns vectors from the embedding service and does not apply local L2 normalization. SQL uses cosine distance, so ranking is based on vector direction rather than raw magnitude. |
| Result score | SQL returns cosine distance. The server converts it to a 0-to-1 relevance score with `1 - (distance / 2)`, clamped to `[0, 1]`, so higher is easier to read as more relevant. |

The normalized Bundle score is for ordering and explanation only. It is not a clinical confidence score, diagnosis probability, or safety classification.

## How does reindex work now, and what limitations remain?

`$reindex` rebuilds standard and vector search indices for existing resources. The processing job pages through resources, refreshes their normal FHIRPath-derived search values, and passes each bounded write batch through the same `IVectorSearchIndexer` used by the normal write path. The indexer resolves enabled definitions plus supported definitions being activated by the current job, extracts and chunks text, and invokes the embedding endpoint. Query resolution remains limited to enabled, searchable definitions.

Schema 118 adds `UpdateResourceSearchParamsWithVectors`. It updates normal search rows and replaces vector rows in one transaction. Vector replacement is limited to current resources whose type, surrogate ID, resource ID, and version still match. If a resource changes during reindex, its newer vector rows are not deleted or overwritten.

An explicit wrapper state distinguishes two empty-vector cases. If vector indexing ran and found no eligible text, reindex removes stale vector rows. If semantic indexing is not registered, the legacy reindex procedure runs and existing vector rows remain untouched. Deployments on schema 117 continue to use that legacy procedure; vector-aware persistence requires schema 118.

Current options for existing resources:

| Scenario | Current behavior |
|---|---|
| New or changed resource after vector search is enabled | Vector rows are generated on the normal SQL write path. |
| Byte-identical `PUT` | Treated as a no-op, but `$reindex` can generate or repair its vector rows without changing the resource. |
| Existing resources after creating a supported vector SearchParameter | Run system `$reindex`; the activation job backfills vectors before making the definition searchable. |
| Model version change | Run `$reindex` to generate vectors under the new `EmbeddingModelId`. |
| Resource changed during reindex | The version-conflicted resource is skipped and can be handled by a subsequent reindex cycle. |
| Vector SearchParameter no longer yields text | Stale vector rows are removed when vector indexing deliberately evaluates the resource. |

Remaining limitations:

1. Updating a referenced Binary does not automatically enqueue each owning DocumentReference. Reindex the owning DocumentReference resources when linked Binary content changes.
2. Reindex remains synchronous with the embedding endpoint within each processing batch, so endpoint availability and throughput affect job duration.
3. Vector-aware persistence is SQL Server schema 118 behavior; schema 117 retains standard-index-only compatibility.

## What are the eventual consistency implications?

With synchronous indexing, semantic search is intended to be consistent when the write returns successfully. The tradeoff is higher write latency and direct dependency on the embedding endpoint during writes.

With the planned asynchronous mode, normal FHIR persistence and standard search indexing would complete before semantic indexing. That creates an eventual-consistency window where:

| During async lag | Expected behavior |
|---|---|
| Normal FHIR read/search | The resource can already appear. |
| Semantic search | The resource might not appear yet, or might appear with older vector evidence until the background job catches up. |
| Resource updates | The worker must check resource version so an older queued job does not overwrite newer vector rows. |
| Deletes | The worker and merge/delete paths must ensure stale vector rows do not survive deleted or superseded resources. |

The desired contract for async mode is eventual semantic completeness with clear operational visibility. The API should either document the possible lag or expose enough indexing status for callers and operators to understand when semantic results may be incomplete.

### Implementation references

* [Built-in R4 vector SearchParameters](../../src/Microsoft.Health.Fhir.Core/Data/R4/ms-search-parameters.json)
* [FHIRPath extraction](../../src/Microsoft.Health.Fhir.Core/Features/Search/TypedElementSearchIndexer.cs)
* [Enabled SearchParameter resolution and validation](../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchParameterResolver.cs)
* [Vector write indexing](../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchIndexer.cs)
* [Direct text and Binary resolution](../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorTextSourceResolver.cs)
* [Vector search configuration validation](../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchConfiguration.cs)
* [Semantic search dependency injection](../../src/Microsoft.Health.Fhir.Shared.Web/Startup.cs)
* [Query-time embedding preparation](../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchQueryProcessor.cs)
* [Embedding model registry](../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlEmbeddingModelRegistry.cs)
* [SQL write integration](../../src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs)