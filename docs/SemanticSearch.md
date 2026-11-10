# SQL semantic search

SQL semantic search adds ordinary FHIR search parameters backed by Azure Foundry embeddings and
SQL Server native vectors. The feature is SQL-only, disabled by default, and currently supports
direct text extracted from the resource being indexed.

## Requirements

- SQL Server 2025 or a supported Azure SQL engine with native vector support.
- An Azure Foundry embedding deployment accessible through the server's configured
  `TokenCredential`.
- A 1,536-dimension cosine embedding model, either one that natively returns 1,536 dimensions
  (`text-embedding-3-small`, `text-embedding-ada-002`) or one that can shorten its output to 1,536
  (`text-embedding-3-large`; see `Embedding:SendDimensionsParameter` below).
- A registered, active, supported SearchParameter with type `special`, at least one base resource
  type, a FHIRPath expression that produces text, and the vector-search configuration extension.

Example configuration:

```json
{
  "FhirServer": {
    "CoreFeatures": {
      "VectorSearch": {
        "Enabled": true,
        "Embedding": {
          "Endpoint": "https://example.services.ai.azure.com",
          "DeploymentName": "text-embedding-3-small",
          "ModelName": "text-embedding-3-small",
          "ModelVersion": "1",
          "Dimensions": 1536,
          "SendDimensionsParameter": false
        },
        "Indexing": {
          "Mode": "Synchronous",
          "ChunkSizeTokens": 800,
          "ChunkOverlapTokens": 100
        },
        "Query": {
          "DistanceMetric": "cosine"
        }
      }
    }
  }
}
```

Invalid enabled configuration fails during service registration. When the feature is disabled,
semantic services are not registered and no embedding request is made. `Query:DistanceMetric` must
be `cosine`; it is matched case-insensitively and stored lowercase in the embedding model registry.
`Embedding:ModelName` (at most 128 characters) and `Embedding:ModelVersion` (at most 64) identify the
model in that registry, so they must be printable ASCII without leading or trailing whitespace. Values
the registry columns could truncate or alter are rejected instead of resolving to another declared model.

`Embedding:Dimensions` must be 1,536, the vector width of the SQL schema. `Embedding:SendDimensionsParameter`
(default `false`) controls whether that value is also sent to the service as the request's `dimensions`
parameter. Leave it `false` for a deployment that natively returns 1,536 dimensions; `text-embedding-ada-002`
rejects the parameter with HTTP 400. Set it to `true` for a model that natively returns more dimensions but can
shorten its output, such as `text-embedding-3-large`. Either way, indexing and querying fail with an explicit
error if the service returns a vector whose length is not 1,536.

### Optional approximate retrieval (DiskANN)

Queries rank with exact cosine distance by default. If an operator has created a version-3 cosine DiskANN
index on `dbo.VectorSearchParam.Embedding`, set `Query:ApproximateSearch` to `Auto` and the server detects
and uses it for relevance-ordered queries (default order or `_sort=_score`); otherwise it stays exact:

```json
"Query": {
  "DistanceMetric": "cosine",
  "ApproximateSearch": "Auto",
  "ApproximateCandidateMultiplier": 10
}
```

Each page ranks at most `(_count + 1) x ApproximateCandidateMultiplier` nearest chunks (1 to 100), so results
are approximate. `_summary=count` and explicit sorts remain exact. The server never creates or drops the index
and re-checks for it every five minutes. See [SQL vector index compatibility](SqlVectorIndexCompatibility.md#opt-in-approximate-search)
for detection, the contract and operations.

## SearchParameter extension

The extension URL is
`http://microsoft.com/fhir/StructureDefinition/vector-search-config`. Its basic direct-text
properties are:

- `extractionPolicy`: `firstValue`, `concatenate`, or `perValueRow`.
- `maxInputTokens`: maximum text input for one configured source.
- `minimumScore`: normalized score threshold from 0 through 1.
- `chunkSizeTokens` and `chunkOverlapTokens`: optional per-parameter chunk overrides.
- `distanceMetric`: must be `cosine`.

Token settings use the tokenizer selected by `Embedding.ModelName`; the server does not fall back to
an approximate character count or a different encoding. Each configured source is truncated to
`maxInputTokens` before token-based chunking. A chunk cannot exceed the provider's 8,192-token
per-input limit.

The SearchParameter must be active and enabled through the existing SearchParameter lifecycle.
First activation uses the normal reindex workflow to backfill existing resources.

A SearchParameter that carries the extension is validated when it is created, updated, or installed from
a package, so the server never accepts a definition it cannot serve. The request fails with HTTP 400 (a
package reports a not-supported issue) unless semantic search is enabled and the definition has type
`special`, FHIR publication status `active`, at least one base resource type, an expression, and valid
effective chunk settings. The same check runs on update, so a vector SearchParameter cannot be moved to
another publication status; delete it instead. The Cosmos DB data store rejects these SearchParameters.

## Indexing and recovery

The MVP uses one configured embedding model for indexing and queries. Storage and transport keys
include `EmbeddingModelId`, allowing model-distinct rows for the same resource, SearchParameter,
and chunk. This does not enable multi-model migration or runtime model selection: replacement,
reindex, and recovery still manage vector rows resource-wide, not independently per model.

Create and update requests extract configured text, chunk it, create embeddings synchronously, and
send vector rows in the same SQL merge operation as the resource and ordinary search indices.
Source text is extracted from the finalized resource, after the server assigns its version and
last-updated time, so an expression such as `meta.versionId` yields the same passage on write and on
reindex. Embedding still happens once per write, outside the persistence retry loop.
Deletes and empty extraction replace prior vector rows with an empty set. Reindex uses the existing `UpdateResourceSearchParams` procedure so ordinary and vector indices are
updated atomically.
Foundry requests are split without reordering when they would exceed 2,048 inputs or 300,000 total
tokens; provider failures and output-count mismatches fail the indexing operation.

If a multi-call resource transaction times out, the transaction watchdog rebuilds search indices
and embeddings before rolling the transaction forward. The vector TVP is sent only to schema 119
or later, preserving new-caller compatibility with older supported schemas. Existing callers may
omit the TVP against current schemas; SQL Server treats an omitted input TVP as empty.

## Query behavior

Use the semantic SearchParameter like any ordinary query parameter:

```http
GET /Observation?semantic-text=difficulty%20breathing&status=final&_count=20
```

The server creates exactly one query embedding, applies ordinary structured filters and resource
authorization, ranks and deduplicates matching resources by native cosine distance, and returns the
normalized value in `Bundle.entry.search.score`. Relevance order is the default. Supported explicit
structured sorts override relevance order, and continuation tokens preserve deterministic paging
with stable resource keys. Search text longer than the provider's 8,192-token per-input limit is
rejected with HTTP 400 before any embedding request is made.

`_sort=_score` is supported only as the sole sort key. A compound sort that includes it, such as
`_sort=_score,_lastUpdated`, is rejected like any other unsupported sort: HTTP 400 under
`Prefer: handling=strict`; otherwise a warning is returned and the default relevance order applies.

Semantic parameters are request-level only. A SMART scope constraint that uses one cannot be
enforced, because the relevance predicate cannot be re-evaluated for resources reached through
`_include` or `_revinclude`. Such a scope never authorizes access on its own: a request that depends
on it is denied with HTTP 403, regardless of `Prefer: handling`, while a request that another scope
fully authorizes is unaffected.

A semantic search does not offer an `_include`/`_revinclude` continuation. Included resources beyond
the include count are truncated and a warning is returned instead of an includes `next` link, and a
`$includes` request that carries a semantic parameter is rejected with HTTP 400.

## Unsupported behavior

This MVP does not support:

- Binary content, PDF extraction, or linked-resource text resolution.
- Forward or reverse semantic-search chains.
- The Patient `$semantic-search` operation.

It does not emit semantic evidence, snippets, witnesses, provenance extensions, or linked-source
refresh jobs.
