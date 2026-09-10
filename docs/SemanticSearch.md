# SQL semantic search

SQL semantic search adds ordinary FHIR search parameters backed by Azure Foundry embeddings and
SQL Server native vectors. The feature is SQL-only, disabled by default, and currently supports
direct text extracted from the resource being indexed.

## Requirements

- SQL Server 2025 or a supported Azure SQL engine with native vector support.
- An Azure Foundry embedding deployment accessible through the server's configured
  `TokenCredential`.
- A 1,536-dimension cosine embedding model.
- A registered, active, supported SearchParameter with type `special`, a FHIRPath expression that
  produces text, and the vector-search configuration extension.

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
          "Dimensions": 1536
        },
        "Indexing": {
          "Mode": "Synchronous",
          "ChunkSizeTokens": 800,
          "ChunkOverlapTokens": 100
        },
        "Query": {
          "DefaultCount": 10,
          "MaxCount": 50,
          "CandidateCount": 100,
          "DistanceMetric": "cosine"
        }
      }
    }
  }
}
```

Invalid enabled configuration fails during service registration. When the feature is disabled,
semantic services are not registered and no embedding request is made.

## SearchParameter extension

The extension URL is
`http://microsoft.com/fhir/StructureDefinition/vector-search-config`. Its basic direct-text
properties are:

- `sourceStrategy`: must be `directText`.
- `extractionPolicy`: `firstValue`, `concatenate`, or `perValueRow`.
- `maxInputTokens`: maximum text input for one configured source.
- `minimumScore`: normalized score threshold from 0 through 1.
- `chunkSizeTokens` and `chunkOverlapTokens`: optional per-parameter chunk overrides.
- `distanceMetric`: must be `cosine`.

The SearchParameter must be active and enabled through the existing SearchParameter lifecycle.
First activation uses the normal reindex workflow to backfill existing resources.

## Indexing and recovery

Create and update requests extract configured text, chunk it, create embeddings synchronously, and
send vector rows in the same SQL merge operation as the resource and ordinary search indices.
Deletes and empty extraction replace prior vector rows with an empty set. Reindex uses
`UpdateResourceSearchParamsWithVectors` so ordinary and vector indices are updated atomically.

If a multi-call resource transaction times out, the transaction watchdog rebuilds search indices
and embeddings before rolling the transaction forward. The vector TVP is sent only to schema 117
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
with stable resource keys.

## Unsupported behavior

This MVP rejects the following before calling the embedding service:

- `localBinaryReference`, Binary content, and PDF extraction.
- Forward or reverse semantic-search chains.
- The Patient `$semantic-search` operation.

It does not emit semantic evidence, snippets, witnesses, provenance extensions, or linked-source
refresh jobs. Those capabilities require their authorization and lifecycle components to be
installed together in a later layer.
