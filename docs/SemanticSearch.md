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

## Deploying with the sample template

[`samples/templates/default-azuredeploy-docker.json`](../samples/templates/default-azuredeploy-docker.json)
can connect a SQL deployment to Azure Foundry. Set `vectorSearchFoundryNewOrExisting` to `new` or
`existing`; the default, `none`, leaves the feature disabled. The template then grants the web app's
system-assigned managed identity the Cognitive Services OpenAI User role on the account and sets the
`FhirServer__CoreFeatures__VectorSearch__*` app settings. The endpoint, model name, and model version are
read from the deployed account and model deployment, and `SendDimensionsParameter` is set to `true` only
for `text-embedding-3-large`. These parameters apply only when `solutionType` is `FhirServerSqlServer`;
they are ignored for Cosmos DB. Set `enableReindex` to `true` so new SearchParameters can be backfilled.

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `vectorSearchFoundryNewOrExisting` | `none` | `none`, `new`, or `existing`. |
| `foundryAccountName` | `<serviceName>-foundry` | Account to create or use. A new account also uses it as its custom subdomain. |
| `foundrySubscriptionId`, `foundryResourceGroup` | The deployment's | Location of the account. The resource group must already exist. |
| `foundryLocation` | The resource group's region | Region for a new account. It must offer the model and deployment type. |
| `foundryRestrictNetworkAccess` | `true` | Whether a new account accepts traffic only from allowed IP addresses. |
| `foundryAdditionalAllowedIpAddresses` | `[]` | Extra IPv4 addresses or CIDR ranges a new account accepts, such as an administrator's address. |
| `embeddingDeploymentName` | The model name | Model deployment to create or use. |
| `embeddingModelName`, `embeddingModelVersion` | `text-embedding-3-small`, `1` | Model for a new deployment. |
| `embeddingDeploymentSkuName`, `embeddingDeploymentCapacity` | `GlobalStandard`, `120` | Deployment type and rate limit, in thousands of tokens per minute, for a new deployment. |

### New account

`new` creates an `AIServices` account (S0) that accepts only Microsoft Entra ID authentication, plus an
embedding deployment with automatic model-version upgrades disabled, because the model version is recorded
with every stored vector. By default, the account firewall denies all traffic except from the web app's
possible outbound IP addresses and `foundryAdditionalAllowedIpAddresses`. The firewall applies to every
data-plane caller, so the Foundry portal playground and your own scripts cannot reach the deployment unless
you add your address. Set `foundryRestrictNetworkAccess` to `false` to accept traffic from all public
networks.

The allowed addresses are captured when the template runs. If the web app's outbound addresses change, for
example after moving to a different App Service pricing tier, redeploy the template or update the firewall.

### Existing account

`existing` only assigns the role and reads the endpoint and model from the existing account. It does not
modify the account, because an ARM deployment of the account would replace its configuration. Before
deploying, make sure that:

- `foundryAccountName` names the account; the `<serviceName>-foundry` default rarely matches an existing one.
- The account has a custom subdomain, which Microsoft Entra ID authentication requires.
- The embedding deployment named by `embeddingDeploymentName` exists. Its model must meet the
  [requirements](#requirements). `embeddingModelName` only supplies the default deployment name, and the
  other `embedding*` parameters are ignored.
- The deploying identity can create role assignments on the account, for example as Owner or User Access
  Administrator.
- If the account is in another resource group or subscription, the deploying identity can also create
  deployments (`Microsoft.Resources/deployments/write`) in that resource group, for example as Contributor.

If the account restricts network access, allow the web app's outbound addresses after deploying:

```bash
for ip in $(az webapp show --subscription <fhir-subscription-id> -g <fhir-resource-group> -n <serviceName> --query possibleOutboundIpAddresses -o tsv | tr ',' ' '); do
  az cognitiveservices account network-rule add --subscription <foundry-subscription-id> -g <foundry-resource-group> -n <foundry-account> --ip-address "$ip"
done
```

With either option, the role assignment can take several minutes to propagate. Until it does, embedding
requests fail with an authorization error.

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

### Example: Composition narrative

[`samples/search-parameters/composition-narrative-semantic.json`](../samples/search-parameters/composition-narrative-semantic.json)
indexes the narrative of a Composition and every nested section:

```text
(Composition.text.`div` | Composition.repeat(section).text.`div`)
    .select(toString().replaceMatches('<[^>]*>', ' ')
        .replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&')
        .replaceMatches('\\s+', ' ').trim())
```

- `div` must be escaped with backticks because it is the FHIRPath integer-division keyword. FHIR's own
  `dom-6` invariant writes `` text.`div` `` for the same reason. The Firely engine accepts an unescaped
  `text.div`, but the Ignixa engine rejects it as a syntax error. A SearchParameter whose expression fails to
  compile is logged as a warning and indexes nothing, so it would silently never match.
- Narrative is `xhtml`, which has no search value of its own. `toString()` converts it to a string, the first
  regular expression removes the markup, and the `replace` calls decode the entities that XML serialization
  produces. Decoding `&amp;` last keeps escaped text such as `&amp;lt;` literal. Other entities are left
  unchanged.
- `perValueRow` embeds the document narrative and each section narrative separately, so a passage never spans
  sections and `maxInputTokens` (8,000) applies to each narrative block rather than to the whole document.

Create the SearchParameter, then run `$reindex` to index existing Compositions and make the parameter
searchable:

```http
POST /SearchParameter
Content-Type: application/fhir+json

<contents of composition-narrative-semantic.json>
```

```http
GET /Composition?narrative-semantic=shortness%20of%20breath
```

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

The query is one natural-language value and is never split on `,`. Source text is extracted through
the shared string indexing path, which unescapes the FHIR search escape sequences `\,`, `\|`, `\$`,
and `\\`. The query text is unescaped the same way, so identical text produces an identical embedding
input whether it was indexed from a resource or sent as a query. Other backslashes are unchanged.

`_sort=_score` is supported only as the sole sort key. A compound sort that includes it, such as
`_sort=_score,_lastUpdated`, is rejected like any other unsupported sort: HTTP 400 under
`Prefer: handling=strict`; otherwise a warning is returned and the default relevance order applies.

Semantic parameters are request-level only. A SMART scope constraint that uses one cannot be
enforced, because the relevance predicate cannot be re-evaluated for resources reached through
`_include` or `_revinclude`. Such a scope is withdrawn rather than merged into the query, so it never
authorizes access on its own. Scopes are alternatives: a request is denied with HTTP 403, regardless
of `Prefer: handling`, only when no other scope authorizes the requested resource types. For example,
given `system/*.rs?semantic-text=x` and `system/Observation.rs`, an `Observation` search is authorized
by the typed scope alone (restricted to that scope's own constraints, if any), whereas a search of
another resource type, or a system-level search, is denied.

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
