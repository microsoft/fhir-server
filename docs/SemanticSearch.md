# Semantic Search (SQL)

Internal engineering reference for the SQL semantic (vector) search capability introduced on the
`feature/sql-semantic-search` branch. This document describes exactly what the branch adds to the
repository, the formal FHIR contracts it defines (the custom SearchParameter, its configuration
extension, the evidence extension, and the Patient operation), the SQL schema, the runtime
configuration, and how the write, query, reindex, and refresh flows work.

This is a reference for the team, not customer-facing documentation. The narrative rationale and the
alternatives that were considered live in the ADR ([docs/arch/adr-2608-sql-semantic-search.md](arch/adr-2608-sql-semantic-search.md))
and in the design spec that accompanies it.

- Status: experimental, SQL Server only, disabled by default.
- Storage contract: SQL Server 2025 native `vector(1536)`, cosine distance, exact nearest-chunk ranking.
- Scope of retrieval: the FHIR server ranks and returns evidence only. Intent detection and answer
  generation are out of scope and remain the caller's responsibility.

---

## 1. At a glance

The branch adds a metadata-driven vector search feature that is expressed entirely through FHIR
SearchParameter metadata, so no resource type or parameter identity is hard-coded.

- A new SearchParameter of type `special` with code `semantic-text`, carrying a Microsoft
  `vector-search-config` extension, marks a resource element as vectorizable and configures how it is
  chunked, embedded, and queried.
- On write, the server extracts the configured text (directly, or from a referenced local `Binary`),
  chunks it, calls an external embedding endpoint, and stores one vector row per chunk. Embedding is
  synchronous and the vectors are committed in the same transaction as the resource.
- On query, `?semantic-text=<text>` combines with ordinary FHIR filters. The structured filters and the
  Patient compartment bound the candidate set, then SQL ranks that set by cosine distance and returns
  each matched resource once with a relevance score and evidence (the exact matched chunk plus its
  provenance).
- A Patient-scoped operation, `POST [base]/Patient/{id}/$semantic-search`, ranks across every
  vector-eligible resource type in the patient compartment in one call.
- `$reindex` backfills vectors for existing resources, and a durable job re-embeds owners when a
  referenced `Binary` changes or is deleted.

---

## 2. Concepts and terminology

- Vector SearchParameter: an ordinary FHIR `SearchParameter` resource of type `special` whose
  `vector-search-config` extension makes its `base`/`expression` a vectorization target. Discovered from
  the live registry; never hard-coded.
- Chunk: a bounded slice of source text (token window with overlap). One embedding and one
  `dbo.VectorSearchParam` row is produced per chunk.
- Embedding model registry: a SQL table (`dbo.EmbeddingModel`) that stamps every vector with the model
  name, version, dimension, and distance metric that produced it.
- Evidence: the per-match record of the exact chunk that ranked, its score and rank, the vector
  SearchParameter canonical, the source resource and element path, and (for chained queries) the witness
  resource whose vector produced the match.
- Source strategy: whether the configured expression yields the text directly (`DirectText`) or a
  reference to a local `Binary` whose content is the text (`LocalBinaryReference`).

---

## 3. Client-facing surfaces

### 3.1 The `semantic-text` SearchParameter (type `special`)

Semantic search is modeled as a FHIR `SearchParameter` of type `special` (the same mechanism the spec
uses for `near` and `_text`). Its code is `semantic-text`. It is not hard-coded: an operator registers a
`SearchParameter` resource whose `base` and `expression` point at the element to vectorize and whose
`vector-search-config` extension configures behavior. The parameter appears in the `CapabilityStatement`
once active, supported, and searchable.

Ordinary resource search, combining structured filters and the semantic predicate:

```http
GET [base]/DocumentReference?patient=Patient/123&date=ge2026-06-05&semantic-text=trouble%20breathing
```

Long queries can be sent as `POST [base]/DocumentReference/_search` with a form body. The response is a
normal `searchset` Bundle. Each match carries a relevance score in `Bundle.entry.search.score` and a
`semantic-search-evidence` extension (section 3.4).

Eligibility rules enforced by `VectorSearchParameterResolver`: a definition is used for indexing and
query only when it is type `special`, carries valid `vector-search-config` metadata, is active, has a
`base` and `expression`, and is both supported and searchable. A newly posted definition that is only
`Supported` is admitted for first-activation backfill but not for query until it is searchable.

Parsing: `SearchParameterExpressionParser` maps a `special` parameter that carries `vector-search-config`
to an immutable `VectorSearchExpression`. Query text is preserved verbatim (including commas) and is
excluded from the plan-shape `ToString()`. Modifiers are rejected. Absent semantic services, an
unregistered canonical, or an inactive/unsupported/non-searchable definition produce
`SearchParameterNotSupportedException`.

### 3.2 The `vector-search-config` SearchParameter extension

Canonical URL: `http://microsoft.com/fhir/StructureDefinition/vector-search-config`.
Model: `Microsoft.Health.Fhir.Core.Models.VectorSearchParameterConfig`. Parsed onto
`SearchParameterInfo.VectorConfig` by `SearchParameterWrapper`.

| Nested extension | Type | Meaning | Default |
|---|---|---|---|
| `extractionPolicy` | code | How expression values become source text: `FirstValue`, `Concatenate`, or `PerValueRow`. | `Concatenate` |
| `sourceStrategy` | code | `DirectText` (value is the text) or `LocalBinaryReference` (value is a `Binary` reference whose content is the text). | `DirectText` |
| `maxInputTokens` | integer | Upper bound on source tokens accepted from this parameter. | `8000` |
| `minimumScore` | decimal | Minimum normalized score (0..1) for a chunk to be eligible. Acts as a maximum cosine-distance predicate. | `0` |
| `chunkSizeTokens` | integer (optional) | Per-parameter chunk size. Falls back to the server default when omitted. | server default |
| `chunkOverlapTokens` | integer (optional) | Per-parameter chunk overlap. Falls back to the server default when omitted. | server default |
| `distanceMetric` | string (optional) | Per-parameter metric. Only `cosine` is supported today. | `cosine` |

`extractionPolicy` values are defined by `VectorTextExtractionPolicy`; `sourceStrategy` values by
`VectorTextSourceStrategy`.

### 3.3 The `$semantic-search` Patient operation

Defined by `Microsoft.Health.Fhir.Core/Data/OperationDefinition/semantic-search.json` and served by
`SemanticSearchController`. Route: `POST [base]/Patient/{id}/$semantic-search`
(`KnownRoutes.SemanticSearchPatientById`). Instance-level on `Patient`, `affectsState = false`,
`experimental = true`.

Input parameters (`Parameters` body):

| Name | Card. | Type | Meaning |
|---|---|---|---|
| `query` | 1..1 | string | Natural-language text to rank the patient compartment by. |
| `type` | 0..* | code | Optional resource types to include. Repeating forms a union. |
| `count` | 0..1 | integer | Maximum globally ranked results. Validated against `Query.MaxCount`. |

Output:

| Name | Card. | Type | Meaning |
|---|---|---|---|
| `return` | 1..1 | Bundle | A `searchset` Bundle of globally ranked resources with semantic evidence. |

Request example:

```http
POST [base]/Patient/123/$semantic-search
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "query", "valueString": "trouble breathing overnight" },
    { "name": "count", "valueInteger": 10 },
    { "name": "type",  "valueCode": "DocumentReference" }
  ]
}
```

Candidate types are the intersection of the FHIR Patient-compartment resource types, the resource types
that have active vector SearchParameters, and any requested `type` values. Requesting an ineligible type
fails explicitly. Candidate selection uses one `ISearchService.SearchCompartmentAsync` query; no resource
type is hard-coded in the controller or handler. `SemanticSearchController` dispatches a
`SemanticSearchRequest` through the mediator; `SemanticSearchHandler` performs compartment selection,
per-type vector ranking, global ranking, evidence authorization, and Bundle construction.

### 3.4 The `semantic-search-evidence` extension

Canonical URL: `http://microsoft.com/fhir/StructureDefinition/semantic-search-evidence`.
Written on `Bundle.entry.search` by `BundleFactory`. Model:
`Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch.SemanticSearchEvidence`.

| Nested extension | Type | Meaning |
|---|---|---|
| `text` | string | The exact matched chunk text. |
| `chunkOrdinal` | integer | Zero-based ordinal of the chunk within the indexed text. |
| `score` | decimal | Normalized chunk relevance (0..1, higher is more relevant). |
| `rank` | positiveInt | One-based rank across all evidence on the current response page, by score descending. |
| `searchParameter` | uri | Canonical of the vector SearchParameter that selected the text. |
| `source` | Reference | Resource that contains the source text (for example a `Binary`). |
| `sourcePath` | string | Element path of the source text (for example `Binary.data` or `Binary.data#page=2`). |
| `witness` | Reference (optional) | The related vector-owning resource that produced a chained match. |

Multiple matching chunks do not duplicate the Bundle entry: each matched resource appears once, and up to
`Query.EvidenceCount` evidence items are attached. Rank is assigned after resource pagination by
`SemanticSearchEvidenceRanker`, so it is stable within a page and restarts across pages.

Serialization note: ordinary search Bundles normally use an optimized raw serializer that emits only
`entry.search.mode`. `FhirJsonOutputFormatter` falls back to the standard Firely serializer when any entry
has `Search.Score` or `Search.Extension`, so score and evidence survive without hard-coding semantic
fields.

### 3.5 Relevance score, ranking, and sorting

`VECTOR_DISTANCE('cosine', a, b)` returns 0 (identical) to 2 (opposite). The server normalizes it to a
0..1 relevance score as `1 - distance / 2`, clamped to `[0,1]`, and writes it to
`Bundle.entry.search.score` and to each evidence `score`.

Ordering contract:

- No `_sort` and `_sort=_score` both order by semantic distance ascending (best score first), with
  resource type and surrogate id as deterministic tie-breakers.
- An explicit ordinary FHIR `_sort` (for example `-_lastUpdated`) overrides relevance ordering while the
  score and evidence remain on each result.
- `_score` is a synthetic sort token accepted only when the completed query contains a vector predicate;
  `-_score`, and `_score` without a vector predicate, are rejected.
- Semantic continuation tokens carry distance, resource type id, and surrogate id, so paging stays stable
  under default and explicit score order.

### 3.6 Semantic chaining (one level)

The server supports exactly one chain hop, forward or reverse, when the vector SearchParameter uses a
direct-text or a linked-Binary source:

- Reverse: `GET [base]/Patient?_has:Observation:subject:semantic-text=<text>` ranks each `Patient` by the
  best matching witness `Observation`.
- Forward: `GET [base]/Observation?subject:Patient.semantic-text=<text>` ranks each `Observation` by its
  referenced `Patient` target's vectors.

Each root resource is returned once (a `CROSS APPLY TOP (1)` selects the best witness), and the evidence
carries the witness reference separately from the source reference. Multi-hop chains and, for chained
queries, linked-Binary reverse chains that require intermediate-witness authorization are rejected before
the embedding call. Multi-hop assembly beyond one level is the caller's responsibility using ordinary FHIR
(`_include`, `_revinclude`, `Encounter/$everything`).

---

## 4. Configuration

All settings live under `FhirServer:CoreFeatures:VectorSearch`
(`Microsoft.Health.Fhir.Core.Configs.VectorSearchConfiguration`) and are validated at startup when the
feature is enabled, so a misconfigured deployment fails fast. The feature is off by default.

```jsonc
{
  "FhirServer": {
    "CoreFeatures": {
      "VectorSearch": {
        "Enabled": true,
        "Embedding": {
          "Endpoint": "https://<resource>.cognitiveservices.azure.com",
          "DeploymentName": "text-embedding-3-small",
          "ModelName": "text-embedding-3-small",
          "ModelVersion": "1",
          "Dimensions": 1536
        },
        "Indexing": {
          "Mode": "Synchronous",
          "ChunkSizeTokens": 800,
          "ChunkOverlapTokens": 100,
          "Pdf": {
            "MaximumFileSizeBytes": 10485760,
            "MaximumPageCount": 200,
            "MaximumExtractedCharacters": 500000,
            "ExtractionTimeout": "00:00:30"
          }
        },
        "Query": {
          "DefaultCount": 10,
          "MaxCount": 50,
          "CandidateCount": 100,
          "EvidenceCount": 3,
          "DistanceMetric": "cosine"
        }
      }
    }
  }
}
```

| Setting | Default | Validation when enabled |
|---|---|---|
| `Enabled` | `false` | When false, vector SearchParameters are inert and no semantic services are registered. |
| `Embedding.Endpoint` | none | Required, must be an absolute HTTPS URI. |
| `Embedding.DeploymentName` | none | Required, non-empty. |
| `Embedding.ModelName` | `text-embedding-3-small` | Required, non-empty. |
| `Embedding.ModelVersion` | none | Required, non-empty. |
| `Embedding.Dimensions` | `1536` | Must equal `1536` (the SQL vector width). |
| `Indexing.Mode` | `Synchronous` | Only `Synchronous` is supported. |
| `Indexing.ChunkSizeTokens` | `800` | Must be greater than zero. |
| `Indexing.ChunkOverlapTokens` | `100` | Non-negative and strictly less than the chunk size. |
| `Indexing.Pdf.MaximumFileSizeBytes` | `10485760` (10 MiB) | Greater than zero. |
| `Indexing.Pdf.MaximumPageCount` | `200` | Greater than zero. |
| `Indexing.Pdf.MaximumExtractedCharacters` | `500000` | Greater than zero. |
| `Indexing.Pdf.ExtractionTimeout` | `00:00:30` | Greater than zero. |
| `Query.DefaultCount` | `10` | Greater than zero. |
| `Query.MaxCount` | `50` | Greater than or equal to `DefaultCount`. |
| `Query.CandidateCount` | `100` | Greater than or equal to `MaxCount`. |
| `Query.EvidenceCount` | `3` | Greater than zero. |
| `Query.DistanceMetric` | `cosine` | Must equal `cosine`. |

Constants `VectorSearchConfiguration.SupportedDimensions = 1536` and
`SupportedDistanceMetric = "cosine"` encode the current storage contract.

---

## 5. SQL schema

The feature adds vector storage keyed the same way as the existing search-parameter tables, so ordinary
structured filters continue to run through the existing tables and only the embedding is new. Schema
versions 117 through 119 are introduced (`SchemaVersionConstants.Max = 119`).

### 5.1 Tables and types

`dbo.VectorSearchParam` (one row per chunk):

```sql
CREATE TABLE dbo.VectorSearchParam
(
    ResourceTypeId          smallint        NOT NULL,
    ResourceSurrogateId     bigint          NOT NULL,
    SearchParamId           smallint        NOT NULL,
    ChunkOrdinal            smallint        NOT NULL,           -- default 0
    EmbeddingModelId        smallint        NOT NULL,
    ChunkText               nvarchar(max)   NOT NULL,           -- exact chunk returned as evidence
    SourceTextHash          binary(32)      NOT NULL,           -- chunk content hash (provenance; reserved for a future reuse optimization)
    SourceResourceTypeId    smallint        NULL,               -- provenance for referenced-source text
    SourceResourceId        varchar(64)     NULL,
    SourceResourceVersion   varchar(64)     NULL,
    SourcePath              nvarchar(512)   NULL,
    Embedding               vector(1536)    NOT NULL,
    CONSTRAINT PKC_VectorSearchParam PRIMARY KEY CLUSTERED
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal)
);

-- Reverse lookup: given a changed source (for example a Binary), find owners to refresh.
CREATE NONCLUSTERED INDEX IX_VectorSearchParam_SourceResource
ON dbo.VectorSearchParam (SourceResourceTypeId, SourceResourceId)
INCLUDE (ResourceTypeId, ResourceSurrogateId)
WHERE SourceResourceTypeId IS NOT NULL AND SourceResourceId IS NOT NULL;
```

`dbo.EmbeddingModel` (model registry, keyed by name and version):

```sql
CREATE TABLE dbo.EmbeddingModel
(
    EmbeddingModelId smallint      IDENTITY(1,1) NOT NULL,
    ModelName        varchar(128)  NOT NULL,
    ModelVersion     varchar(64)   NOT NULL,
    Dimension        int           NOT NULL,
    DistanceMetric   varchar(16)   NOT NULL,   -- default 'cosine'
    CreatedAt        datetime2(7)  NOT NULL,   -- default sysutcdatetime()
    CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (EmbeddingModelId),
    CONSTRAINT U_EmbeddingModel_Name_Version UNIQUE (ModelName, ModelVersion)
);
```

`dbo.VectorSearchParamList` is the table-valued parameter used to send chunk rows to the merge
procedures. Its `Embedding` column is passed as `nvarchar(max)` JSON and cast to `vector(1536)` in SQL.

The chunking policy is not stored on `EmbeddingModel`. It comes from configuration and, optionally, from
the per-parameter `vector-search-config` extension. A chunk-size change is treated like a model change and
triggers re-embedding of affected rows on the next write or reindex.

The embedding endpoint and deployment name are deployment configuration (`VectorSearch.Embedding`), not
durable model metadata: `EmbeddingModel` stores only the model name, version, dimension, and distance
metric that produced each vector.

### 5.2 Stored procedures

- `MergeResources`, `MergeResourcesAndSearchParams`: extended to accept and persist the vector TVP.
- `UpdateResourceSearchParamsWithVectors` (v118): wraps ordinary and vector index updates in one
  transaction for reindex, replacing vectors only for the current type, surrogate, resource id, and
  version.
- `MergeResourcesWithVectorSearchSourceRefresh`,
  `MergeResourcesAndSearchParamsWithVectorSearchSourceRefresh`,
  `MergeResourcesDeleteResourceWithVectorSearchSourceRefresh` (v119): enqueue linked-source refresh work
  atomically at the owning procedure's commit boundary on write and hard delete.
- `EnqueueVectorSearchSourceRefreshJobs`, `GetVectorSearchSourceDependencies` (v119): enqueue refresh jobs
  and resolve the owners that depend on a changed source.

### 5.3 Schema version milestones

- V117: `dbo.VectorSearchParam`, `dbo.EmbeddingModel`, `dbo.VectorSearchParamList`, and the merge-path
  wiring that persists vectors on write.
- V118 (`VectorSearchReindexVersion`): `UpdateResourceSearchParamsWithVectors` for vector-aware
  `$reindex`.
- V119 (`VectorSearchSourceRefreshVersion`): durable linked-source refresh (reindex queue type, job type,
  reverse-dependency lookup, and the refresh worker).

---

## 6. How it works

### 6.1 Write and index flow

Triggered synchronously inside the resource write in `SqlServerFhirDataStore` when semantic search is
registered and enabled.

1. `TypedElementSearchIndexer.Extract` produces search-index entries. Vector parameters (type `special`
   with `VectorConfig`) are filtered out of the ordinary search-value buckets at the two SQL persistence
   sites (`MergeSearchParameterRowGenerator`, `ResourceWriteClaimListRowGenerator`) so they do not flow
   into the token/string tables, but the entries remain visible to the vector indexer.
2. `VectorTextSourceResolver` resolves source text for each vector parameter. For `DirectText` it uses the
   extracted value; for `LocalBinaryReference` it resolves the referenced `Binary` and decodes its content
   through an `IBinaryContentExtractor` selected by MIME type (`PlainTextBinaryContentExtractor` for
   `text/plain`, `PdfBinaryContentExtractor` for `application/pdf`). PDF extraction is bounded by the
   `Pdf` limits and emits one segment per page with `page=N` provenance. Unsupported or non-text content
   is skipped without failing the write.
3. Text is normalized (whitespace, line endings, control characters) so an unchanged note produces a
   stable `SourceTextHash`.
4. `TextChunker` splits the text into ordered, overlapping chunks using the active chunk size and overlap.
5. Every chunk is embedded by `IEmbeddingClient` (`AzureFoundryEmbeddingClient`) on each write. The
   indexer does not currently skip unchanged chunks: it re-extracts, re-chunks, and re-embeds all chunks
   and replaces the resource's vectors. Every produced vector is stamped with the `EmbeddingModelId`
   resolved by `SqlEmbeddingModelRegistry` from `(ModelName, ModelVersion)`, and each chunk stores a
   `SourceTextHash` so a future reuse optimization can skip re-embedding only when both the hash and the
   active `EmbeddingModelId` match (a hash match alone must not reuse a vector produced by a different
   model).
6. `VectorSearchIndexer.IndexAsync` produces the vector index entries; `VectorSearchParamListRowGenerator`
   turns them into TVP rows; the merge procedures replace the resource's vectors in the same transaction
   as the resource. Only the current version is vectorized.

`ResourceWrapper.VectorSearchIndicesUpdated` distinguishes an intentional empty vector result (delete
stale rows) from semantic indexing being disabled (preserve existing vectors).

### 6.2 Query and rank flow

1. `SearchParameterExpressionParser` recognizes `semantic-text` and builds a `VectorSearchExpression`,
   preserving any one-level chain relationship around the vector leaf.
2. `VectorSearchQueryProcessor` prepares the query once, before retries and query-cache races: it finds
   exactly one vector expression, embeds the query text through `IEmbeddingClient`, validates the fixed
   1536-dimension contract, resolves the SQL-local `EmbeddingModelId`, and returns an immutable
   `PreparedVectorSearchQuery` (including `PreparedVectorSearchChainLink`s for chained queries). Duplicate
   vector expressions are rejected before any external call.
3. `RemoveVectorSearchRewriter` removes the vector leaf from the structured predicate tree while leaving
   the structured filters intact, so the SQL query filters candidates first.
4. `SqlQueryGenerator` emits SQL that applies the structured filters and Patient compartment, then ranks
   the bounded candidate set (`Query.CandidateCount`) by `VECTOR_DISTANCE('cosine', ...)`. Candidates are
   deduplicated by resource using each resource's highest-scoring chunk (a `CROSS APPLY TOP (1)` per
   owner), so each resource is returned once, and the resource limit (`count`) is applied to that
   deduplicated set. Additional chunks for a returned resource may still be attached as evidence (up to
   `Query.EvidenceCount`). `SqlVectorStore`, `SqlDocumentReferenceSemanticSearch`, and
   `SqlVectorResourceReader` execute and read results; `SqlVectorFormatter` handles the vector parameter
   encoding.
5. `SearchResultEntry` carries the score and the winning evidence. `SemanticSearchEvidenceRanker` assigns
   page ranks. `SemanticSearchEvidenceFilter` authorizes evidence sources (section 7).
6. `BundleFactory` writes `Bundle.entry.search.score` and the `semantic-search-evidence` extension.

The Patient operation follows the same ranking and evidence path per candidate type, then ranks globally
across types in `SemanticSearchHandler`.

### 6.3 Reindex backfill

`$reindex` populates vectors for resources written before the feature or before a parameter was
activated. `ReindexProcessingJob` invokes `IVectorSearchIndexer` once per configured write batch before
SQL persistence, reusing the write-path extraction, chunking, and model logic, and persists through
`UpdateResourceSearchParamsWithVectors` (v118). Version-conflicted resources are excluded from vector
deletion and insertion and remain eligible for a later cycle. `VectorSearchParameterResolver`'s indexing
view admits definitions that are enabled or explicitly `Supported`, so the same activation job can
backfill a posted definition before it becomes searchable.

### 6.4 Linked-source refresh

Because a `DocumentReference` and its `Binary` are separate resources that can change independently,
schema v119 adds durable refresh. Writes and hard deletes enqueue refresh work atomically at the owning
SQL procedure's commit boundary. `SqlVectorSearchSourceDependencyStore` resolves the owners that depend on
a changed source through the reverse-dependency index, and `VectorSearchSourceRefreshJob` reloads the
current owners, re-extracts and re-embeds their text, and persists the derived vectors without changing
the owner's FHIR version. Refresh job definitions retain the source version so `EnqueueJobs`
deduplication does not suppress later source updates; rapid source churn can produce redundant refresh
work and is an accepted operational limitation.

---

## 7. Security and authorization

- Ordinary FHIR resource authorization and the Patient compartment run first, so a caller only ranks and
  receives resources it is permitted to see. Vectors live in the same database and inherit that model.
- Evidence authorization is fail-closed (`SemanticSearchEvidenceFilter`): a scored result is removed
  entirely if any of its evidence sources (or, for chained queries, the witness) is denied, missing,
  malformed, unsupported, or returns no authorized match. Survivors are reranked and `TotalCount` is
  cleared whenever filtering changes membership, so no passage, score, or count leaks.
- Count-only exact totals are restricted for `LocalBinaryReference` vector parameters, because count-only
  rows carry no source provenance to authorize; direct-text vector counts are unchanged.
- The embedding endpoint is reached with a managed identity (`DefaultAzureCredential`, no stored key). The
  caller identity requires the Cognitive Services OpenAI User role. Note text sent for embedding stays
  within the resource, region, and agreement that governs the endpoint.

---

## 8. Component and dependency-injection map

Registration happens in `Startup.AddSemanticSearch`. It is skipped, and no semantic services are
registered, unless the runtime data store is SQL Server and `VectorSearch.Enabled` is true. When active it
binds:

| Abstraction | Implementation | Lifetime |
|---|---|---|
| `IVectorSearchParameterResolver` | `VectorSearchParameterResolver` | Singleton |
| `IEmbeddingClient` | `AzureFoundryEmbeddingClient` | Scoped |
| `IVectorStore` | `SqlVectorStore` | Scoped |
| `IEmbeddingModelRegistry` | `SqlEmbeddingModelRegistry` | Singleton |
| `IVectorSearchIndexer` | `VectorSearchIndexer` | Scoped |
| `IVectorSearchQueryProcessor` | `VectorSearchQueryProcessor` | Scoped |
| `ISemanticSearchEvidenceFilter` | `SemanticSearchEvidenceFilter` | Transient (SearchModule) |

`DeterministicEmbeddingClient` is a deterministic stand-in used by tests and offline scenarios so ranking
behavior can be asserted without network access.

Core abstractions (`Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch`): `IEmbeddingClient`,
`IEmbeddingModelRegistry`, `IVectorStore`, `IVectorSearchIndexer`, `IVectorSearchQueryProcessor`,
`IVectorSearchParameterResolver`, `IVectorSearchSourceDependencyStore`, `IVectorResourceReader`,
`IVectorTextSourceResolver`, `IBinaryContentExtractor`, `ITextChunker`, `ISemanticSearchEvidenceFilter`,
plus the value types `VectorSearchChunk`, `VectorSearchHit`, `VectorSearchResult`,
`VectorSearchIndexEntry`, `VectorTextSource`, `BinaryContentSegment`, `PreparedVectorSearchQuery`,
`PreparedVectorSearchChainLink`, and `SemanticSearchEvidence`.

---

## 9. Testing

- Core unit tests: configuration validation, evidence model and evidence filter, text chunker, embedding
  client, vector indexer, parameter resolver, query processor, and text-source resolver
  (`Microsoft.Health.Fhir.Core.UnitTests`, `Microsoft.Health.Fhir.Azure.UnitTests`).
- Shared/R4 tests: expression parsing (`VectorSearchExpressionParserTests`), parameter definition parsing,
  `BundleFactory` score and evidence serialization, and the `SemanticSearchHandler` and
  `SemanticSearchController` behavior.
- SQL Server tests: `SqlVectorStore`, `RemoveVectorSearchRewriter`, the source-refresh job and schema
  gating, and the parameter validator.
- End-to-end: `SemanticSearchTests` in `Microsoft.Health.Fhir.R4.Tests.E2E` exercise the Patient operation
  and compartment membership over Observation, DiagnosticReport, DocumentReference, and Coverage. E2E
  execution requires a SQL Server 2025 instance with native `vector` support.

Test runner note: this repo uses Microsoft Testing Platform. Pass filters after `--`, for example
`dotnet test <project> -- --filter "FullyQualifiedName~SemanticSearch"`. Some environments discover zero
tests via `dotnet test`; running the built test dll directly is a reliable workaround.

---

## 10. Build-time dependency and known limitation

The `dbo.VectorSearchParam` table declares a `vector(1536)` column. Generating the SQL schema model
classes requires the shared `Microsoft.Health.Extensions.BuildTimeCodeGenerator` to recognize the `vector`
column type, which the released generator does not. The fix lives in an unreleased shared-components
change: [microsoft/healthcare-shared-components#1449](https://github.com/microsoft/healthcare-shared-components/pull/1449),
"Support VECTOR columns in generated SQL schema models," developed on branch
`users/t-annag/fix-vector-schema-model` (2 commits over tag `v11.0.128`).

Until that ships, the branch builds only against a locally built preview package
(`1.0.0-fix-vector-schema-model-20260720-144944-preview`) served from a local NuGet feed, with
`HealthcareSharedPackageVersion` pinned to it for local and demo builds. That pin, and the machine-local
NuGet feed entry in `nuget.config`, must be reverted before any upstream pull request. To make the feature
build against released packages, decouple it from the generator (exclude the vector tables from generated
models and hand-author the row and table types, or model the column with a generator-supported type and
`CAST` to `vector(1536)` in SQL). See ADR-2608 for details.

### 10.1 Current branch state and how to un-pin

This branch is intentionally left pinned to the local preview package so it builds and demos as-is;
un-pinning is deliberately deferred to whoever finishes the feature. Two machine-local settings carry the
pin:

- `Directory.Packages.props`: `HealthcareSharedPackageVersion` is set to
  `1.0.0-fix-vector-schema-model-20260720-144944-preview` (for reference, `main` uses `11.0.135`).
- `nuget.config`: a `Local NuGet Feed` source points at a local `.nuget-local` directory that holds that
  preview `Microsoft.Health.*` package set.

To build this branch locally today, keep both settings and populate the local feed with the preview
package set.

To un-pin once the generator fix ships in an official package (PR #1449 or an equivalent):

1. In `Directory.Packages.props`, set `HealthcareSharedPackageVersion` to the official released version
   that contains the fix (the same property `main` uses, for example a release after `11.0.135`).
2. In `nuget.config`, remove the `Local NuGet Feed` source and its `packageSourceMapping` entry.
3. Build against the released packages; no local feed is required.

---

## 11. File inventory

Semantic-search files introduced or changed by the branch, by area. Test files are omitted here for
brevity; see section 9.

Core (`src/Microsoft.Health.Fhir.Core`):

- `Configs/VectorSearchConfiguration.cs`, `VectorSearchEmbeddingConfiguration.cs`,
  `VectorSearchIndexingConfiguration.cs`, `VectorSearchIndexingMode.cs`, `VectorSearchPdfConfiguration.cs`,
  `VectorSearchQueryConfiguration.cs`.
- `Models/VectorSearchParameterConfig.cs`, `VectorTextExtractionPolicy.cs`, `VectorTextSourceStrategy.cs`,
  and `SearchParameterInfo.cs` (adds `VectorConfig`).
- `Features/Search/SemanticSearch/` (all interfaces and implementations listed in section 8, plus
  `VectorSearchIndexer.cs`, `VectorSearchQueryProcessor.cs`, `VectorSearchParameterResolver.cs`,
  `VectorTextSourceResolver.cs`, `TextChunker.cs`, `PlainTextBinaryContentExtractor.cs`,
  `PdfBinaryContentExtractor.cs`, `SemanticSearchEvidence.cs`, `SemanticSearchEvidenceFilter.cs`,
  `SemanticSearchEvidenceRanker.cs`, `DeterministicEmbeddingClient.cs`).
- `Features/Search/Expressions/VectorSearchExpression.cs`,
  `Features/Search/Expressions/Parsers/SearchParameterExpressionParser.cs`,
  `Features/Search/SearchParameterInfoExtensions.cs`, `Features/Search/SearchParameterNames.cs`,
  `Features/Search/SearchResultEntry.cs` (adds score and evidence).
- `Features/Persistence/ResourceWrapper.cs` (vector indices), `BundleWrappers/SearchParameterWrapper.cs`
  (parses `vector-search-config`).
- `Features/Operations/Reindex/ReindexProcessingJob.cs`,
  `Features/Operations/Reindex/VectorSearchSourceRefreshJobDefinition.cs`.
- `Messages/SemanticSearch/SemanticSearchRequest.cs`, `SemanticSearchResponse.cs`.
- `Data/OperationDefinition/semantic-search.json`.

Shared Core (`src/Microsoft.Health.Fhir.Shared.Core`):

- `Features/Search/SemanticSearch/SemanticSearchHandler.cs`, `Features/Search/BundleFactory.cs`,
  `Features/Search/Parameters/SearchParameterToTypeResolver.cs` (supports `toString()`).

Shared API and Web (`src/Microsoft.Health.Fhir.Shared.Api`, `src/Microsoft.Health.Fhir.Shared.Web`):

- `Controllers/SemanticSearchController.cs`, `Startup.cs` (`AddSemanticSearch`).

Azure (`src/Microsoft.Health.Fhir.Azure`):

- `SemanticSearch/AzureFoundryEmbeddingClient.cs`.

SQL Server (`src/Microsoft.Health.Fhir.SqlServer`):

- `Features/Search/SemanticSearch/SqlVectorStore.cs`, `SqlDocumentReferenceSemanticSearch.cs`,
  `SqlEmbeddingModelRegistry.cs`, `SqlVectorResourceReader.cs`, `SqlVectorFormatter.cs`.
- `Features/Storage/SqlServerFhirDataStore.cs`, `SqlVectorSearchSourceDependencyStore.cs`,
  `TvpRowGeneration/Merge/VectorSearchParamListRowGenerator.cs`,
  `TvpRowGeneration/Merge/MergeSearchParameterRowGenerator.cs`.
- `Features/Operations/VectorSearchSourceRefreshJob.cs`,
  `Features/Search/Expressions/Visitors/RemoveVectorSearchRewriter.cs`,
  `Features/Search/SqlServerSearchParameterValidator.cs`.
- `Features/Schema/SchemaVersion.cs`, `SchemaVersionConstants.cs`, and the SQL under
  `Features/Schema/Sql/Tables/VectorSearchParam.sql`, `Sql/Types/VectorSearchParamList.sql`, and the
  `Sql/Sprocs/*VectorSearch*` and merge procedures listed in section 5.2.

---

## 12. References

- ADR-2608, FHIR-Native SQL Semantic Search: [docs/arch/adr-2608-sql-semantic-search.md](arch/adr-2608-sql-semantic-search.md).
- Design spec, "Semantic Search over FHIR Clinical Documents" (health-paas-docs PR 65041).
- Build-time generator dependency: [microsoft/healthcare-shared-components#1449](https://github.com/microsoft/healthcare-shared-components/pull/1449).
- SQL schema versioning: [docs/SchemaVersioning.md](SchemaVersioning.md).
- Search architecture: [docs/SearchArchitecture.md](SearchArchitecture.md).
