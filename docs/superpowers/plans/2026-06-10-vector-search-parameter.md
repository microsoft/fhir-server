# Vector Search Parameter (ADR 2605) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement ADR 2605 — a vector (semantic similarity) search parameter over FHIR text fields, with operator-selectable **synchronous or asynchronous** embedding persistence.

**Architecture:** A new `VectorSearchValue` ISearchValue kind flows through the existing extraction → ResourceWrapper → MergeResources pipeline into a new `dbo.VectorSearchParam` table (Azure SQL `VECTOR` type + ANN index). In `Synchronous` mode an enricher fills embeddings (Azure AI Foundry, managed identity) before the merge transaction. In `Asynchronous` mode the write path does nothing vector-specific: a lease-based `VectorizationWatchdog` tracks a watermark over `dbo.Transactions` (in `dbo.Parameters`, the `InvisibleHistoryCleanupWatchdog` pattern) and enqueues range-batched `EmbeddingGeneration` jobs onto the existing `dbo.JobQueue`, processed idempotently by an `EmbeddingGenerationJob` (`IJob`). A standard `Prefer: embedding-persistence=synchronous` preference (RFC 7240) requests best-effort inline embedding per write in async mode, with `Preference-Applied` signaling whether it was honored. The read path adds a `VectorSearchExpression` parsed from the standard `?param=text` URL shape (no modifier), translated to an ANN-then-filter SQL query, with similarity scores surfaced via `Bundle.entry.search.score`.

**Tech Stack:** .NET (existing FHIR Server solution), Azure SQL Database `VECTOR(1536)` + `VECTOR_DISTANCE`, Azure AI Foundry embeddings via `Azure.Identity` `DefaultAzureCredential`, existing SQL watchdog/lease infrastructure, schema version **V114**.

**Read first:**
- `docs/arch/Proposals/adr-2605-vector-search-parameter.md` (the spec — §3 defines the two persistence modes)
- `docs/SchemaVersioning.md` (schema migration rules)

---

## Phase map (each phase compiles and passes tests independently)

| Phase | Delivers | Ship gate |
|-------|----------|-----------|
| 1 | Configuration + feature flag | No behavior change when disabled |
| 2 | Core model: `VectorSearchValue`, SP extension parsing, extraction policies | Indexer produces vector values in-memory; nothing persisted |
| 3 | `IEmbeddingClient` + Foundry implementation + circuit breaker | Client testable in isolation |
| 4 | SQL schema V114 (tables, TVPs, MergeResources changes, capability probe) | Migration applies; tables empty |
| 5 | Synchronous write path + per-request header override | Sync mode end-to-end writes vector rows |
| 6 | Asynchronous write path (watermark watchdog + JobQueue job) | Async mode end-to-end |
| 7 | Read path (expression, parser, SQL gen, scoring, pagination) | Vector queries work in both modes |
| 8 | Lifecycle & ops ($import skip, reindex, capability statement, telemetry) | Feature complete |

## File structure (created/modified across all phases)

```
src/Microsoft.Health.Fhir.Core/
  Configs/VectorSearchConfiguration.cs                                  [create, P1]
  Configs/CoreFeatureConfiguration.cs                                   [modify, P1]
  Features/Operations/QueueType.cs                                      [modify, P6 — EmbeddingGeneration = 7]
  Features/Operations/EmbeddingGeneration/EmbeddingGenerationJobDefinition.cs [create, P6]
  Features/Operations/EmbeddingGeneration/EmbeddingGenerationJob.cs     [create, P6]
  Models/SearchParameterInfo.cs                                         [modify, P2]
  Models/VectorSearchParameterConfig.cs                                 [create, P2]
  Features/Definition/SearchParameterDefinitionBuilder.cs               [modify, P2]
  Features/Search/SearchValues/VectorSearchValue.cs                     [create, P2]
  Features/Search/SearchValues/ISearchValueVisitor.cs                   [modify, P2]
  Features/Search/TypedElementSearchIndexer.cs                          [modify, P2]
  Features/Search/SearchParameterInfoExtensions.cs                      [modify, P2]
  Features/Search/VectorSearch/IEmbeddingClient.cs                      [create, P3]
  Features/Search/Expressions/VectorSearchExpression.cs                 [create, P7]
  Features/Search/Expressions/IExpressionVisitor.cs                     [modify, P7]
  Features/Search/Expressions/DefaultExpressionVisitor.cs               [modify, P7]
  Features/Search/Expressions/Parsers/SearchParameterExpressionParser.cs[modify, P7]
  Features/Search/SearchResultEntry.cs                                  [modify, P7]
src/Microsoft.Health.Fhir.Azure/
  VectorSearch/AzureAIFoundryEmbeddingClient.cs                         [create, P3]
src/Microsoft.Health.Fhir.SqlServer/
  Features/Schema/SchemaVersionConstants.cs                             [modify, P4]
  Features/Schema/Migrations/114.diff.sql                               [create, P4]
  Features/Schema/Sql/Tables/VectorSearchParam.sql                      [create, P4]
  Features/Schema/Sql/Tables/EmbeddingModel.sql                         [create, P4]
  Features/Schema/Sql/Types/VectorSearchParamList.sql                   [create, P4]
  Features/Schema/Sql/Sprocs/MergeResources.sql                         [modify, P4]
  Features/Storage/VectorSearchCapabilityProvider.cs                    [create, P4]
  Features/Storage/TvpRowGeneration/Merge/VectorSearchParamListRowGenerator.cs   [create, P5]
  Features/Storage/SqlServerFhirDataStore.cs                            [modify, P5]
  Features/Storage/VectorEmbeddingEnricher.cs                           [create, P5]
  Features/Watchdogs/VectorizationWatchdog.cs                           [create, P6]
  Features/Watchdogs/WatchdogsBackgroundService.cs                      [modify, P6]
  Features/Search/Expressions/Visitors/QueryGenerators/VectorSearchParamQueryGenerator.cs [create, P7]
  Features/Search/Expressions/Visitors/SqlRootExpressionRewriter.cs     [modify, P7]
  Features/Search/SqlServerSearchService.cs                             [modify, P7]
src/Microsoft.Health.Fhir.Shared.Core/
  Features/Search/SearchOptionsFactory.cs                               [modify, P7]
  Features/Search/BundleFactory.cs                                      [modify, P7]
```

Naming used consistently throughout this plan:
- Config: `VectorSearchConfiguration`, enum `EmbeddingPersistenceMode { Synchronous, Asynchronous }`
- Value: `VectorSearchValue` (properties `SourceText`, `ChunkOrdinal`, `Embedding`, `SourceTextHash`)
- SP config: `VectorSearchParameterConfig` (properties `ExtractionPolicy`, `MaxInputTokens`), enum `VectorTextExtractionPolicy { FirstValue, Concatenate, PerValueRow }`
- Client: `IEmbeddingClient.EmbedAsync(IReadOnlyList<string> inputs, bool tightBudget, CancellationToken) → Task<IReadOnlyList<float[]>>` (`tightBudget: true` = single attempt, short timeout — used by the Prefer-driven path)
- Expression: `VectorSearchExpression` (properties `Parameter`, `QueryText`, `K`, `MinScore`, `QueryEmbedding`)
- Async job: `QueueType.EmbeddingGeneration = 7`; `EmbeddingGenerationJobDefinition { TypeId, ResourceTypeId, StartSurrogateId, EndSurrogateId }`
- Preference: `Prefer: embedding-persistence=synchronous` → best-effort inline embed; `Preference-Applied: embedding-persistence=synchronous` echoed on success (RFC 7240; ADR §3.4)

---

## Phase 1 — Configuration

### Task 1.1: VectorSearchConfiguration

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Configs/VectorSearchConfiguration.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Configs/CoreFeatureConfiguration.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Configs/VectorSearchConfigurationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
public class VectorSearchConfigurationTests
{
    [Fact]
    public void GivenDefaultConfiguration_VectorSearchIsDisabledAndSynchronous()
    {
        var config = new CoreFeatureConfiguration();

        Assert.NotNull(config.VectorSearch);
        Assert.False(config.VectorSearch.Enabled);
        Assert.False(config.VectorSearch.Suspended);
        Assert.Equal(EmbeddingPersistenceMode.Synchronous, config.VectorSearch.EmbeddingPersistenceMode);
        Assert.Equal(1536, config.VectorSearch.EmbeddingDimensions);
        Assert.Equal(200, config.VectorSearch.DefaultK);
    }
}
```

- [ ] **Step 2: Run test, verify it fails** (`VectorSearch` property does not exist)

Run: `dotnet test src/Microsoft.Health.Fhir.Core.UnitTests --filter VectorSearchConfigurationTests`
Expected: compile failure / FAIL

- [ ] **Step 3: Implement**

`VectorSearchConfiguration.cs`:

```csharp
namespace Microsoft.Health.Fhir.Core.Configs
{
    public enum EmbeddingPersistenceMode
    {
        Synchronous,
        Asynchronous,
    }

    public class VectorSearchConfiguration
    {
        public bool Enabled { get; set; }

        // Kill switch: embedding calls fail fast with 503 at read/write paths without a restart.
        public bool Suspended { get; set; }

        public EmbeddingPersistenceMode EmbeddingPersistenceMode { get; set; } = EmbeddingPersistenceMode.Synchronous;

        public Uri EmbeddingEndpoint { get; set; }

        public string EmbeddingDeploymentName { get; set; }

        public int EmbeddingDimensions { get; set; } = 1536;

        public int DefaultK { get; set; } = 200;

        public double OversamplingFactor { get; set; } = 2.0;

        public int MaxQueryTextLength { get; set; } = 4096;

        // Asynchronous-mode knob: max resources per EmbeddingGeneration job definition.
        // Watchdog period/lease are tuned via the standard dbo.Parameters watchdog rows, not here.
        public int JobBatchSize { get; set; } = 500;
    }
}
```

In `CoreFeatureConfiguration.cs` add:

```csharp
public VectorSearchConfiguration VectorSearch { get; set; } = new VectorSearchConfiguration();
```

Binding comes for free via the existing `FhirServer:Core` section bind; config path is `FhirServer:Core:VectorSearch:*`.

- [ ] **Step 4: Run test, verify PASS**
- [ ] **Step 5: Commit** — `feat: add VectorSearch configuration section (ADR 2605)`

---

## Phase 2 — Core model

### Task 2.1: VectorSearchValue + visitor

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Search/SearchValues/VectorSearchValue.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/SearchValues/ISearchValueVisitor.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SearchValues/VectorSearchValueTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
public class VectorSearchValueTests
{
    [Fact]
    public void GivenSourceText_HashIsDeterministicSha256()
    {
        var a = new VectorSearchValue("shortness of breath", chunkOrdinal: 0);
        var b = new VectorSearchValue("shortness of breath", chunkOrdinal: 0);

        Assert.Equal(32, a.SourceTextHash.Length);
        Assert.Equal(a.SourceTextHash, b.SourceTextHash);
        Assert.Null(a.Embedding);
        Assert.False(a.IsValidAsCompositeComponent);
    }

    [Fact]
    public void GivenEmbeddingSet_ValueExposesIt()
    {
        var value = new VectorSearchValue("text", 0);
        value.SetEmbedding(new float[] { 0.1f, 0.2f });
        Assert.Equal(2, value.Embedding.Length);
    }

    [Fact]
    public void GivenWhitespaceText_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new VectorSearchValue("   ", 0));
    }
}
```

- [ ] **Step 2: Run, verify FAIL** (type missing)
- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// A search value representing text to be (or already) embedded for vector similarity search.
    /// Embedding is null until filled by the synchronous enricher or the asynchronous drain.
    /// </summary>
    public class VectorSearchValue : ISearchValue
    {
        public VectorSearchValue(string sourceText, int chunkOrdinal)
        {
            EnsureArg.IsNotNullOrWhiteSpace(sourceText, nameof(sourceText));
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));

            SourceText = sourceText;
            ChunkOrdinal = chunkOrdinal;
            SourceTextHash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText));
        }

        public string SourceText { get; }

        public int ChunkOrdinal { get; }

        public byte[] SourceTextHash { get; }

        public float[] Embedding { get; private set; }

        public bool IsValidAsCompositeComponent => false;

        public void SetEmbedding(float[] embedding)
        {
            EnsureArg.IsNotNull(embedding, nameof(embedding));
            Embedding = embedding;
        }

        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            visitor.Visit(this);
        }

        public override string ToString() => $"vector(chunk:{ChunkOrdinal})";
    }
}
```

Add to `ISearchValueVisitor.cs`:

```csharp
void Visit(VectorSearchValue vector);
```

- [ ] **Step 4: Fix all implementers of `ISearchValueVisitor` revealed by the compiler.** For every visitor that should never see a vector value (e.g. `SearchValueExpressionBuilderHelper`, Cosmos visitors), implement as a throw:

```csharp
public void Visit(VectorSearchValue vector)
    => throw new InvalidOperationException("Vector search values are handled by a dedicated pipeline.");
```

Build the full solution to find them: `dotnet build Microsoft.Health.Fhir.sln`

- [ ] **Step 5: Run tests, verify PASS**
- [ ] **Step 6: Commit** — `feat: add VectorSearchValue search value kind`

### Task 2.2: Parse `vector-search-config` extension into SearchParameterInfo

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Models/VectorSearchParameterConfig.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Models/SearchParameterInfo.cs` (add property)
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Definition/SearchParameterDefinitionBuilder.cs` (parse extension)
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/SearchParameterInfoExtensions.cs` (hash)
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Definition/SearchParameterDefinitionBuilderTests.cs` (extend existing class)

- [ ] **Step 1: Write the failing test.** Use a SearchParameter JSON fixture with `type=special` and the extension:

```json
{
  "resourceType": "SearchParameter",
  "id": "Observation-clinical-note",
  "url": "http://example.org/fhir/SearchParameter/Observation-clinical-note",
  "name": "clinicalNote",
  "code": "clinicalNote",
  "status": "active",
  "base": ["Observation"],
  "type": "special",
  "expression": "Observation.note.text",
  "extension": [{
    "url": "http://microsoft.com/fhir/StructureDefinition/vector-search-config",
    "extension": [
      { "url": "kind", "valueCode": "vector" },
      { "url": "extractionPolicy", "valueCode": "concatenate" },
      { "url": "maxInputTokens", "valueInteger": 8000 }
    ]
  }]
}
```

```csharp
[Fact]
public void GivenSearchParameterWithVectorConfigExtension_WhenBuilt_VectorConfigIsPopulated()
{
    SearchParameterInfo info = BuildFromFixture("SearchParameterVectorNote.json"); // helper mirrors existing builder tests

    Assert.NotNull(info.VectorConfig);
    Assert.Equal(VectorTextExtractionPolicy.Concatenate, info.VectorConfig.ExtractionPolicy);
    Assert.Equal(8000, info.VectorConfig.MaxInputTokens);
    Assert.Equal(SearchParamType.Special, info.Type);
}

[Fact]
public void GivenSpecialSearchParameterWithoutExtension_VectorConfigIsNull()
{
    SearchParameterInfo info = BuildFromFixture("SearchParameterNear.json");
    Assert.Null(info.VectorConfig);
}
```

- [ ] **Step 2: Run, verify FAIL**
- [ ] **Step 3: Implement**

`VectorSearchParameterConfig.cs`:

```csharp
namespace Microsoft.Health.Fhir.Core.Models
{
    public enum VectorTextExtractionPolicy
    {
        FirstValue,
        Concatenate,
        PerValueRow,
    }

    public class VectorSearchParameterConfig
    {
        public const string ExtensionUrl = "http://microsoft.com/fhir/StructureDefinition/vector-search-config";

        public VectorTextExtractionPolicy ExtractionPolicy { get; set; } = VectorTextExtractionPolicy.Concatenate;

        public int MaxInputTokens { get; set; } = 8000;
    }
}
```

`SearchParameterInfo.cs` — add:

```csharp
public VectorSearchParameterConfig VectorConfig { get; set; }
```

In `SearchParameterDefinitionBuilder` where the `SearchParameterWrapper` is converted to `SearchParameterInfo`, read the extension off the wrapped `ITypedElement` (follow the existing pattern used for other extensions in the builder; sub-extensions `extractionPolicy` and `maxInputTokens` map to the config properties; unknown `extractionPolicy` codes throw `InvalidDefinitionException`).

`SearchParameterInfoExtensions.CalculateSearchParameterHash` — include vector config in the hashed string so altering it triggers reindex semantics:

```csharp
// inside the per-parameter concatenation:
if (searchParamInfo.VectorConfig != null)
{
    sb.Append(searchParamInfo.VectorConfig.ExtractionPolicy).Append(searchParamInfo.VectorConfig.MaxInputTokens);
}
```

- [ ] **Step 4: Run tests, verify PASS** (including existing hash tests — hash for non-vector SPs must be unchanged)
- [ ] **Step 5: Commit** — `feat: parse vector-search-config extension into SearchParameterInfo`

### Task 2.3: Extraction in TypedElementSearchIndexer (policies)

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/TypedElementSearchIndexer.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/TypedElementSearchIndexerTests.cs` (extend)

Vector SPs do not go through the converter registry: in `ProcessNonCompositeSearchParameter`, when `searchParameter.VectorConfig != null`, evaluate the FHIRPath expression, collect the string values of the resulting nodes, apply the extraction policy, and yield `VectorSearchValue` entries directly.

- [ ] **Step 1: Write the failing tests** (Observation with two `note.text` values):

```csharp
[Fact]
public void GivenVectorSpWithConcatenatePolicy_TwoNotes_YieldsSingleJoinedValue()
{
    IReadOnlyCollection<SearchIndexEntry> entries = ExtractWithVectorSp(policy: VectorTextExtractionPolicy.Concatenate,
        notes: new[] { "first note", "second note" });

    var value = Assert.IsType<VectorSearchValue>(Assert.Single(entries).Value);
    Assert.Equal("first note\nsecond note", value.SourceText);
    Assert.Equal(0, value.ChunkOrdinal);
}

[Fact]
public void GivenVectorSpWithPerValueRowPolicy_TwoNotes_YieldsTwoValuesWithOrdinals()
{
    var entries = ExtractWithVectorSp(VectorTextExtractionPolicy.PerValueRow, new[] { "a", "b" });
    var values = entries.Select(e => (VectorSearchValue)e.Value).OrderBy(v => v.ChunkOrdinal).ToList();
    Assert.Equal(2, values.Count);
    Assert.Equal(0, values[0].ChunkOrdinal);
    Assert.Equal(1, values[1].ChunkOrdinal);
}

[Fact]
public void GivenVectorSpWithFirstValuePolicy_TwoNotes_YieldsFirstOnly()
{
    var entries = ExtractWithVectorSp(VectorTextExtractionPolicy.FirstValue, new[] { "a", "b" });
    Assert.Equal("a", ((VectorSearchValue)Assert.Single(entries).Value).SourceText);
}

[Fact]
public void GivenVectorSpAndWhitespaceOnlyText_YieldsNoEntries()
{
    var entries = ExtractWithVectorSp(VectorTextExtractionPolicy.Concatenate, new[] { "   " });
    Assert.Empty(entries);
}
```

- [ ] **Step 2: Run, verify FAIL**
- [ ] **Step 3: Implement** in `TypedElementSearchIndexer.ProcessNonCompositeSearchParameter` — branch before converter lookup:

```csharp
if (searchParameter.VectorConfig != null)
{
    return ExtractVectorSearchValues(searchParameter, resource, context);
}
```

```csharp
private static IEnumerable<SearchIndexEntry> ExtractVectorSearchValues(
    SearchParameterInfo searchParameter,
    ITypedElement resource,
    EvaluationContext context)
{
    IReadOnlyList<string> texts = resource
        .Select(searchParameter.Expression, context)
        .Select(node => node.Value?.ToString())
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .ToList();

    if (texts.Count == 0)
    {
        yield break;
    }

    switch (searchParameter.VectorConfig.ExtractionPolicy)
    {
        case VectorTextExtractionPolicy.FirstValue:
            yield return new SearchIndexEntry(searchParameter, new VectorSearchValue(texts[0], 0));
            break;
        case VectorTextExtractionPolicy.Concatenate:
            yield return new SearchIndexEntry(searchParameter, new VectorSearchValue(string.Join('\n', texts), 0));
            break;
        case VectorTextExtractionPolicy.PerValueRow:
            for (int i = 0; i < texts.Count; i++)
            {
                yield return new SearchIndexEntry(searchParameter, new VectorSearchValue(texts[i], i));
            }

            break;
    }
}
```

Note: `MaxInputTokens` truncation is applied at the embedding client boundary (Task 3.1), not here — the indexer stores full source text in memory only; the hash must be computed over what is actually embedded, so truncation happens before hashing in the enricher. **Correction for implementers:** truncate in `ExtractVectorSearchValues` (truncate `texts[i]` to a character budget of `MaxInputTokens * 4` as an approximation) so `SourceTextHash` matches the embedded input. Keep the truncation helper in `VectorSearchValue` as a static method `Truncate(string, int maxTokens)`.

- [ ] **Step 4: Run tests, verify PASS**
- [ ] **Step 5: Commit** — `feat: extract VectorSearchValue entries with extraction policies`

---

## Phase 3 — Embedding client

### Task 3.1: IEmbeddingClient abstraction + suspension gate

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Search/VectorSearch/IEmbeddingClient.cs`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Search/VectorSearch/EmbeddingUnavailableException.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/VectorSearch/SuspendableEmbeddingClientTests.cs`

- [ ] **Step 1: Define the contract**

```csharp
namespace Microsoft.Health.Fhir.Core.Features.Search.VectorSearch
{
    /// <summary>
    /// Produces embeddings for vector search. One implementation (Azure AI Foundry);
    /// the interface exists for testability, not provider pluggability (ADR 2605 §E3).
    /// </summary>
    public interface IEmbeddingClient
    {
        /// <param name="tightBudget">
        /// True = single attempt with a short timeout, no backoff — for the best-effort
        /// Prefer-driven path where falling back to async is cheaper than waiting.
        /// False = full retry policy + circuit breaker (sync mode, async jobs, query embedding).
        /// </param>
        /// <exception cref="EmbeddingUnavailableException">Surfaced as HTTP 503 by sync-mode/query callers.</exception>
        Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, bool tightBudget, CancellationToken cancellationToken);
    }
}
```

`EmbeddingUnavailableException` is a `FhirException`-derived type carrying an `OperationOutcomeIssue` with `IssueType.Transient`, mapped to 503 by the existing exception middleware (find the existing 503 mapping — `ServiceUnavailableException` handling — and register this type alongside it).

- [ ] **Step 2: Write the failing test for the suspension decorator**

```csharp
[Fact]
public async Task GivenSuspendedConfig_EmbedAsyncThrowsEmbeddingUnavailable_WithoutCallingInner()
{
    var inner = Substitute.For<IEmbeddingClient>();
    var config = new VectorSearchConfiguration { Enabled = true, Suspended = true };
    var client = new SuspendableEmbeddingClient(inner, Options.Create(new CoreFeatureConfiguration { VectorSearch = config }));

    await Assert.ThrowsAsync<EmbeddingUnavailableException>(
        () => client.EmbedAsync(new[] { "x" }, tightBudget: false, CancellationToken.None));
    await inner.DidNotReceiveWithAnyArgs().EmbedAsync(default, default, default);
}
```

- [ ] **Step 3: Implement `SuspendableEmbeddingClient`** (same folder) — a decorator that checks `Suspended` (via `IOptionsMonitor<CoreFeatureConfiguration>` so flips don't need a restart) then delegates.
- [ ] **Step 4: Run tests, verify PASS**
- [ ] **Step 5: Commit** — `feat: IEmbeddingClient abstraction with suspension kill switch`

### Task 3.2: AzureAIFoundryEmbeddingClient

**Files:**
- Create: `src/Microsoft.Health.Fhir.Azure/VectorSearch/AzureAIFoundryEmbeddingClient.cs`
- Modify: DI registration in `src/Microsoft.Health.Fhir.Shared.Api/Modules/SearchModule.cs` (or a new `VectorSearchModule`)
- Test: `src/Microsoft.Health.Fhir.Azure.UnitTests/VectorSearch/AzureAIFoundryEmbeddingClientTests.cs`

Implementation notes (verify exact SDK surface against current `Azure.AI.OpenAI` docs before coding — use the microsoft-docs skill):
- `AzureOpenAIClient(endpoint, new DefaultAzureCredential())` → `GetEmbeddingClient(deploymentName)` → `GenerateEmbeddingsAsync(inputs)`.
- **Batch chunking inside the client:** the endpoint accepts arrays up to 2,048 inputs / 300,000 aggregate tokens per request (8,192 tokens per input) — chunk the input list by both limits and concatenate results in order. Callers (enricher, async job) always pass the full list and never chunk themselves.
- Expose an overload/option for a **tight budget** (single attempt, short timeout, no backoff) used by the `Prefer`-driven best-effort path (Task 5.3); the default policy (full retry + circuit breaker) is used by sync mode and the async job.
- Wrap with Polly: retry (3 attempts, jittered exponential backoff, on 429/5xx/timeouts) inside a circuit breaker (break after 5 consecutive failures, 30s break). On open circuit or exhausted retries throw `EmbeddingUnavailableException`.
- Truncate each input to `MaxInputTokens` budget (same `VectorSearchValue.Truncate` helper as Task 2.3).
- Emit telemetry: call count, latency, status codes (use the existing metric-handler pattern under `Microsoft.Health.Fhir.Core/Logging/Metrics`).
- Unit tests use a stubbed transport/inner callable — never a live endpoint (ADR §9).

- [ ] **Step 1: Write failing tests** — retry-then-success, circuit-open throws `EmbeddingUnavailableException`, truncation applied.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Register in DI** gated on `config.VectorSearch.Enabled` — when disabled, register a `NotSupportedEmbeddingClient` that always throws, so nothing constructs Azure clients.
- [ ] **Step 4: Run tests, verify PASS**
- [ ] **Step 5: Commit** — `feat: Azure AI Foundry embedding client with retry and circuit breaker`

---

## Phase 4 — SQL schema V114

### Task 4.1: Schema objects + migration

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/SchemaVersionConstants.cs` (Max → V114, add `SchemaVersion.V114`, add `VectorSearch = (int)SchemaVersion.V114`)
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj` (`LatestSchemaVersion` 114)
- Create: `Features/Schema/Sql/Tables/EmbeddingModel.sql`, `Tables/VectorSearchParam.sql`, `Types/VectorSearchParamList.sql`
- Create: `Features/Schema/Migrations/114.diff.sql`
- Modify: `Features/Schema/Sql/Sprocs/MergeResources.sql`

Follow `docs/SchemaVersioning.md` exactly (full-schema snapshot regeneration is part of the build; the V114 model classes regenerate from VLatest).

- [ ] **Step 1: Table DDL**

`EmbeddingModel.sql`:

```sql
CREATE TABLE dbo.EmbeddingModel
(
    EmbeddingModelId    SMALLINT IDENTITY(1,1) NOT NULL,
    ModelName           VARCHAR(128)  NOT NULL,
    ModelVersion        VARCHAR(64)   NOT NULL,
    Dimension           INT           NOT NULL,
    DistanceMetric      VARCHAR(16)   NOT NULL CONSTRAINT DF_EmbeddingModel_DistanceMetric DEFAULT 'cosine',
    CreatedAt           DATETIME2(7)  NOT NULL CONSTRAINT DF_EmbeddingModel_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (EmbeddingModelId),
    CONSTRAINT U_EmbeddingModel_Name_Version UNIQUE (ModelName, ModelVersion)
)
```

`VectorSearchParam.sql` (dimension fixed at deploy time; 1536 default — see ADR §5):

```sql
CREATE TABLE dbo.VectorSearchParam
(
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    ChunkOrdinal        SMALLINT      NOT NULL CONSTRAINT DF_VectorSearchParam_ChunkOrdinal DEFAULT 0,
    Embedding           VECTOR(1536)  NOT NULL,
    EmbeddingModelId    SMALLINT      NOT NULL,
    SourceTextHash      BINARY(32)    NULL
)

ALTER TABLE dbo.VectorSearchParam ADD CONSTRAINT PKC_VectorSearchParam
    PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal)
    WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
```

ANN index DDL is **deferred to first feature enablement** (created by `VectorSearchCapabilityProvider` at startup when enabled, not in the migration) because `CREATE VECTOR INDEX` syntax is preview and engine-dependent. Record the exact `CREATE VECTOR INDEX` statement used in the provider with a comment pointing at the Azure SQL preview docs.

TVP type (no async-mode SQL objects are needed — async state is one `dbo.Parameters` watermark row plus ordinary `dbo.JobQueue` rows):

```sql
CREATE TYPE dbo.VectorSearchParamList AS TABLE
(
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    ChunkOrdinal        SMALLINT      NOT NULL,
    Embedding           VARCHAR(MAX)  NOT NULL, -- JSON array; CAST to VECTOR(1536) at insert
    EmbeddingModelId    SMALLINT      NOT NULL,
    SourceTextHash      BINARY(32)    NULL
)
```

(TVPs cannot carry `VECTOR` columns in current preview; pass the JSON literal `'[0.1,0.2,...]'` and `CAST(Embedding AS VECTOR(1536))` in the insert. Verify against current Azure SQL docs at implementation time; if TVP `VECTOR` support has shipped, use it directly.)

- [ ] **Step 2: MergeResources changes.** Add parameters (defaulted so existing callers are unaffected, matching the pattern of prior additive params):

```sql
,@VectorSearchParams dbo.VectorSearchParamList READONLY
```

In the old-version delete block, alongside the other `*SearchParam` deletes:

```sql
DELETE FROM dbo.VectorSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
```

In the insert block:

```sql
INSERT INTO dbo.VectorSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, Embedding, EmbeddingModelId, SourceTextHash)
  SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, CAST(Embedding AS VECTOR(1536)), EmbeddingModelId, SourceTextHash
    FROM @VectorSearchParams
```

Mirror the insert in the `@IsRetry = 1` path with a `WHERE NOT EXISTS` guard, copying the existing per-table retry pattern.

- [ ] **Step 3: `114.diff.sql`** — the tables, types, index, and `ALTER PROCEDURE dbo.MergeResources`. **Engine gating:** wrap `CREATE TABLE dbo.VectorSearchParam` in dynamic SQL guarded by a vector-capability probe so the migration applies cleanly on engines without `VECTOR`:

```sql
IF EXISTS (SELECT 1 FROM sys.types WHERE name = 'vector')
BEGIN
  EXEC('CREATE TABLE dbo.VectorSearchParam (... as above ...)');
END
-- EmbeddingModel is a plain table: create unconditionally.
```

The C# write/read paths never touch `VectorSearchParam` unless `VectorSearch:Enabled = true`, and `VectorSearchCapabilityProvider` (Step 5) fails startup loudly if enabled without engine support — that is the ADR Open Question §10.1 resolution: **probe + explicit flag; misconfiguration fails fast at startup.**

- [ ] **Step 4: Update generated model** — run the schema build target so `VLatest.Generated.*.cs` picks up the new tables/TVPs; add `VectorSearch = (int)SchemaVersion.V114` to `SchemaVersionConstants`.
- [ ] **Step 5: `VectorSearchCapabilityProvider`** (`Features/Storage/`): at startup when `Enabled`, (a) probe `SELECT 1 FROM sys.types WHERE name = 'vector'` — throw a fatal, descriptive exception if unsupported; (b) ensure the ANN index exists (create if missing); (c) upsert the configured model into `dbo.EmbeddingModel` and cache the current `EmbeddingModelId` for row generation and querying. Schema-gate everything on `_schemaInformation.Current >= SchemaVersionConstants.VectorSearch`.
- [ ] **Step 6: Run SQL integration test suite** (existing `Microsoft.Health.Fhir.SqlServer.Tests.Integration`) to confirm the migration applies and MergeResources still passes with the defaulted params.
- [ ] **Step 7: Commit** — `feat: schema V114 - VectorSearchParam and EmbeddingModel`

---

## Phase 5 — Synchronous write path

### Task 5.1: Row generator

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/VectorSearchParamListRowGenerator.cs`
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/VectorSearchParamListRowGeneratorTests.cs`

Subclass `MergeSearchParameterRowGenerator<VectorSearchValue, VectorSearchParamListRow>` following `StringSearchParamListRowGenerator` exactly. `TryGenerateRow` returns `false` (no row) when `searchValue.Embedding == null` — that is the async-mode case, where the resource commits with no vector rows and the watchdog/job pipeline (Phase 6) produces them afterwards. Serialize the embedding as a JSON float array (`[0.1,0.2,...]`) into the TVP's `VARCHAR(MAX)` column; stamp `EmbeddingModelId` from `VectorSearchCapabilityProvider`'s cached id.

- [ ] **Step 1: Failing tests** — embedding present → row with correct JSON/ordinal/hash; embedding null → no row; duplicate (param, ordinal) deduplicated via the base-class HashSet.
- [ ] **Step 2: Implement; register the generator and wire the new TVP into the MergeResources command construction** (find where `StringSearchParamListRowGenerator` output binds to the `@StringSearchParams` parameter and mirror it).
- [ ] **Step 3: Tests PASS; commit** — `feat: VectorSearchParam TVP row generation`

### Task 5.2: Synchronous embedding enricher

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/VectorEmbeddingEnricher.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs`
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/VectorEmbeddingEnricherTests.cs`

The enricher runs in `SqlServerFhirDataStore.MergeAsync` **before** the SQL transaction opens (ADR §3.1: Foundry latency must not hold write locks; covers single upserts and transaction bundles, which both flow through merge).

```csharp
public class VectorEmbeddingEnricher
{
    // ctor: IEmbeddingClient, IOptionsMonitor<CoreFeatureConfiguration>,
    //       RequestContextAccessor<IFhirRequestContext>, IVectorSearchMetricHandler

    /// <summary>
    /// Fills Embedding on every VectorSearchValue in the given wrappers.
    /// Runs when feature enabled AND (mode == Synchronous OR the request carries
    /// Prefer: embedding-persistence=synchronous — see Task 5.3).
    /// Sync mode: failure throws EmbeddingUnavailableException (write fails 503) — caller
    /// must not have opened a transaction yet.
    /// Prefer-driven (async mode): best-effort with tight budget — failure is swallowed,
    /// values stay unembedded, the write commits, and the watermark pipeline picks it up.
    /// </summary>
    public async Task<bool> EnrichAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
    {
        var config = _options.CurrentValue.VectorSearch;
        bool syncMode = config.EmbeddingPersistenceMode == EmbeddingPersistenceMode.Synchronous;
        if (!config.Enabled || (!syncMode && !RequestPrefersSynchronous()))
        {
            return false;
        }

        List<VectorSearchValue> pending = resources
            .SelectMany(r => r.Wrapper.SearchIndices ?? Array.Empty<SearchIndexEntry>())
            .Select(e => e.Value)
            .OfType<VectorSearchValue>()
            .Where(v => v.Embedding == null)
            .ToList();

        if (pending.Count == 0)
        {
            return true; // nothing to embed — preference trivially satisfied
        }

        try
        {
            // One batched call for the whole request/bundle (client chunks internally per
            // endpoint limits: 2048 inputs / 300k aggregate tokens — Task 3.2).
            // Prefer-driven path uses the tight single-attempt budget.
            IReadOnlyList<float[]> embeddings = await _embeddingClient.EmbedAsync(
                pending.Select(v => v.SourceText).ToList(),
                tightBudget: !syncMode,
                cancellationToken);

            for (int i = 0; i < pending.Count; i++)
            {
                pending[i].SetEmbedding(embeddings[i]);
            }

            return true;
        }
        catch (EmbeddingUnavailableException) when (!syncMode)
        {
            // Best-effort preference not honored: commit without vector rows;
            // the watermark pipeline embeds shortly after. Caller omits Preference-Applied.
            return false;
        }
    }
}
```

The `bool` return is "was inline embedding fully applied" — the API layer uses it to emit `Preference-Applied` (Task 5.3). Gate the call site on a new `MergeOptions`-style flag `SkipVectorEmbedding` (set by `$import` — Task 8.1). All-or-nothing bundle semantics fall out automatically in sync mode: the exception propagates before any SQL work.

- [ ] **Step 1: Failing tests** — sync mode embeds all pending values in one batch call; async mode (no preference) makes zero client calls; disabled makes zero calls; sync-mode client failure propagates `EmbeddingUnavailableException`; preference-driven client failure returns `false` and leaves values unembedded (write proceeds).
- [ ] **Step 2: Implement + wire into `MergeAsync`.** `RequestPrefersSynchronous()` parses the `Prefer` header from the current `IFhirRequestContext` for the `embedding-persistence=synchronous` token (Task 5.3); returns false when there is no request context (background jobs).
- [ ] **Step 3: Conditional no-op update reuse:** confirm the existing "resource unchanged" short-circuit path skips enrichment (it never reaches merge with new indices) — add a unit test pinning that.
- [ ] **Step 4: Tests PASS; commit** — `feat: synchronous embedding enrichment before merge transaction`
- [ ] **Step 5: Integration test** (gated on vector-capable DB, see Phase 9 gating): upsert Observation with note in sync mode with stub client → row in `VectorSearchParam`; update resource → old row replaced; delete → row gone.

### Task 5.3: `Prefer: embedding-persistence=synchronous` (RFC 7240)

**Files:**
- Modify: where FHIR `Prefer` values are parsed today (locate the existing `Prefer: return=` / `handling=` parsing — grep `"Prefer"` under `src/Microsoft.Health.Fhir.Api` and `Shared.Api`; extend that parser rather than adding a second one). Add the parsed preference to `IFhirRequestContext` properties so the enricher can read it.
- Modify: the response path for create/update/bundle to emit `Preference-Applied: embedding-persistence=synchronous` when the enricher returned `true` **and** the preference was requested.
- Test: enricher unit tests + API-layer tests for parsing and `Preference-Applied`

Semantics (ADR §3.4) — best-effort, never an error:
- Async mode + preference → inline embedding attempt with the **tight budget** (`tightBudget: true`); success → `Preference-Applied` emitted; failure/timeout → write commits anyway, no `Preference-Applied`, the watermark pipeline vectorizes shortly after.
- Sync mode + preference → already satisfied; `Preference-Applied` echoed. `embedding-persistence=asynchronous` is silently ignored in every mode (RFC 7240: unhonorable preferences are ignored; clients cannot downgrade the operator's consistency choice).
- Unknown values of the `embedding-persistence` token are ignored, like any unrecognized RFC 7240 preference; existing `Prefer` tokens (`return=`, `handling=`) are unaffected — pin with a test.
- Preference while feature disabled → ignored (no `Preference-Applied`); not an error.
- Transaction bundle: one batched embedding call for the whole bundle; `Preference-Applied` only if everything embedded inline.
- The later watchdog job for an honored preference no-ops via the hash short-circuit — Phase 9 E2E pins that (no second embedding call).

- [ ] **Step 1: Failing tests** — async mode + preference → inline embedding call with tight budget + `Preference-Applied` present; embedding failure → 200/201 commit, no `Preference-Applied`; `asynchronous` value ignored; coexists with `Prefer: return=representation`; no request context (job path) → no preference.
- [ ] **Step 2: Implement; tests PASS.**
- [ ] **Step 3: Commit** — `feat: Prefer embedding-persistence=synchronous best-effort inline embedding`

---

## Phase 6 — Asynchronous write path (watermark watchdog + JobQueue)

No write-path changes in this phase: async-mode work is derived from the existing `dbo.Transactions` log after commit. Required reading before implementing: `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/InvisibleHistoryCleanupWatchdog.cs` (the watermark pattern — `dbo.Parameters` row, `MergeResourcesGetTransactionVisibilityAsync`, `GetTransactionsAsync(lastTranId, visibility)`), and `DefragWatchdog.cs` (a watchdog enqueueing JobQueue work).

### Task 6.1: QueueType + job definition

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Operations/QueueType.cs` — add `EmbeddingGeneration = 7,`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Operations/EmbeddingGeneration/EmbeddingGenerationJobDefinition.cs`
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Operations/EmbeddingGeneration/EmbeddingGenerationJobDefinitionTests.cs`

```csharp
namespace Microsoft.Health.Fhir.Core.Features.Operations.EmbeddingGeneration
{
    public class EmbeddingGenerationJobDefinition : IJobData
    {
        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; set; } // (int)JobType.EmbeddingGenerationProcessing — add to the JobType enum alongside existing values

        [JsonProperty("resourceTypeId")]
        public short ResourceTypeId { get; set; }

        [JsonProperty("startSurrogateId")]
        public long StartSurrogateId { get; set; }

        [JsonProperty("endSurrogateId")]
        public long EndSurrogateId { get; set; }
    }
}
```

No PHI in the definition — identity range only (ADR §3.2). Match the exact `IJobData`/`JobRecordProperties` serialization conventions of an existing definition (e.g. the bulk-delete one) when implementing.

- [ ] **Step 1: Failing round-trip serialization test; Step 2: implement; Step 3: PASS; commit** — `feat: EmbeddingGeneration queue type and job definition`

### Task 6.2: VectorizationWatchdog (enqueue-only)

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/VectorizationWatchdog.cs`
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/WatchdogsBackgroundService.cs` (register, gated on `VectorSearch.Enabled`)
- Test: `src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Watchdogs/VectorizationWatchdogTests.cs` + integration tests

Subclass `Watchdog<VectorizationWatchdog>` — single active enqueuer per cluster via the standard lease. The watchdog **only enqueues**; all embedding work happens in jobs. `RunWorkAsync`, mirroring `InvisibleHistoryCleanupWatchdog.RunWorkAsync` line-for-line where possible:

1. Watermark: `Parameters` row `VectorizationWatchdog.LastProcessedTransactionId` (initialize in `InitAdditionalParamsAsync` exactly as `InvisibleHistoryCleanupWatchdog` initializes `LastCleanedUpTransactionId`).
2. `visibility = _store.MergeResourcesGetTransactionVisibilityAsync(...)`.
3. `transToProcess = _store.GetTransactionsAsync(watermark, visibility, ...)`; if empty, emit lag telemetry and return.
4. Resolve the surrogate-id window covered by those transactions; for each resource type that has ≥1 enabled vector SP, split into ranges of ≤ `JobBatchSize` resources and `EnqueueAsync(QueueType.EmbeddingGeneration, definitions, ...)` via `IQueueClient`.
5. Advance the watermark to `transToProcess.Max(t => t.TransactionId)` **after** successful enqueue. (Crash between enqueue and advance → duplicate jobs → harmless, jobs are idempotent.)
6. Telemetry: `VectorizationLag` (visibility − watermark age), ranges enqueued. Queue depth/stale jobs come free from the existing job-monitoring watchdog once the new queue type exists — verify it picks up QueueType 7 without changes.

Runs whenever the feature is enabled, regardless of persistence mode (mode-switch backlog draining, ADR §3.3); in sync mode the enqueued jobs no-op cheaply on the hash short-circuit.

- [ ] **Step 1: Failing unit tests** — no transactions → no enqueue + no watermark move; transactions present → correctly batched definitions per vector-SP resource type; watermark advances only after enqueue; enqueue failure → watermark unchanged.
- [ ] **Step 2: Implement; register in `WatchdogsBackgroundService`.**
- [ ] **Step 3: Tests PASS; commit** — `feat: VectorizationWatchdog enqueues embedding jobs from transaction watermark`

### Task 6.3: EmbeddingGenerationJob

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Operations/EmbeddingGeneration/EmbeddingGenerationJob.cs` (`IJob`, registered like the bulk-delete/reindex processing jobs so existing JobHosting workers dequeue it)
- Modify: DI/job registration module where other `IJob`s are registered
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Operations/EmbeddingGeneration/EmbeddingGenerationJobTests.cs` + SQL integration tests

`ExecuteAsync(JobInfo jobInfo, CancellationToken)`:

1. Deserialize `EmbeddingGenerationJobDefinition`.
2. Load **current, non-deleted** resources in `(ResourceTypeId, StartSurrogateId..EndSurrogateId)` — reuse the existing range-read used by export/defrag (`GetResourcesByTypeAndSurrogateIdRange`). Superseded/deleted versions simply don't come back; nothing to discard.
3. Re-extract via `ISearchIndexer.Extract` (same indexer as the write path → same policies, truncation, hashes). Resources whose types have no vector SP yield nothing — skip.
4. **Idempotency short-circuit:** read existing `VectorSearchParam` rows for the batch; skip any resource whose stored `(SourceTextHash, EmbeddingModelId)` set already matches the extracted set (covers header-forced sync writes, watchdog re-enqueues, job retries, and sync-mode sweeps).
5. Batch `IEmbeddingClient.EmbedAsync` over remaining texts; `SetEmbedding` on each value.
6. Per resource, transactionally delete-then-insert its `VectorSearchParam` rows (reuse the `VectorSearchParamListRowGenerator` TVP + a small targeted merge sproc or parameterized statement — NOT MergeResources; the resource row itself is untouched).
7. Heartbeat via the standard job-hosting wrapper; on embedding failure throw — JobQueue retry/poison semantics handle the rest (`Failed` status visible in job APIs and queue metrics).
8. Telemetry: resources processed/skipped/embedded, rows written.

- [ ] **Step 1: Failing unit tests** — hash-match skip (no client call); extraction-empty skip; embed failure throws (job framework retries); delete-then-insert ordering.
- [ ] **Step 2: Implement + register.**
- [ ] **Step 3: Integration test:** async-mode upsert → no vector row; run watchdog cycle + job → vector row present; re-run job → zero embedding calls (idempotent); update resource then run → rows reflect new text.
- [ ] **Step 4: Tests PASS; commit** — `feat: EmbeddingGenerationJob processes embedding ranges from JobQueue`

---

## Phase 7 — Read path

### Task 7.1: VectorSearchExpression + visitor surface

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/VectorSearchExpression.cs`
- Modify: `IExpressionVisitor.cs`, `DefaultExpressionVisitor.cs` (and compiler-revealed visitors)
- Test: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/Expressions/VectorSearchExpressionTests.cs`

```csharp
public class VectorSearchExpression : Expression
{
    public VectorSearchExpression(SearchParameterInfo parameter, string queryText, int? k, double? minScore)
    {
        Parameter = EnsureArg.IsNotNull(parameter, nameof(parameter));
        QueryText = EnsureArg.IsNotNullOrWhiteSpace(queryText, nameof(queryText));
        K = k;
        MinScore = minScore;
    }

    public SearchParameterInfo Parameter { get; }

    public string QueryText { get; }

    public int? K { get; }

    public double? MinScore { get; }

    /// <summary>Filled by the search service immediately before SQL generation; never serialized.</summary>
    public float[] QueryEmbedding { get; private set; }

    public void SetQueryEmbedding(float[] embedding) => QueryEmbedding = EnsureArg.IsNotNull(embedding, nameof(embedding));

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        => visitor.VisitVectorSearch(this, context);

    public override string ToString() => $"(VectorSearch {Parameter.Code} k:{K})";

    public override void AddValueInsensitiveProperties(HashCode hashCode) { hashCode.Add(Parameter.Url); }
}
```

Add `VisitVectorSearch` to `IExpressionVisitor`; give `DefaultExpressionVisitor` a base implementation so only visitors that care must override. Cosmos visitors throw `SearchOperationNotSupportedException` ("vector search requires SQL Server persistence").

- [ ] Steps: failing visitor-dispatch test → implement → compiler sweep of visitor implementations → PASS → commit `feat: VectorSearchExpression`

### Task 7.2: Parser routing + modifier rejection + `_vectorK`/`_vectorMinScore`

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/Parsers/SearchParameterExpressionParser.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Search/SearchOptionsFactory.cs`
- Test: extend `SearchParameterExpressionParserTests` and `SearchOptionsFactoryTests`

In `SearchParameterExpressionParser.Parse`, before the `SearchParamType` switch:

```csharp
if (searchParameter.VectorConfig != null)
{
    if (modifier != null && modifier.SearchModifierCode != SearchModifierCode.Missing)
    {
        throw new InvalidSearchOperationException(
            string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
    }

    if (modifier?.SearchModifierCode == SearchModifierCode.Missing)
    {
        // fall through to existing missing handling — works as on any SP
    }
    else
    {
        return Expression.SearchParameter(searchParameter,
            new VectorSearchExpression(searchParameter, value, k: null, minScore: null));
    }
}
```

In `SearchOptionsFactory`: recognize reserved `_vectorK` / `_vectorMinScore` query params, validate (`_vectorK` positive int ≤ server max; `_vectorMinScore` in [0,1]), and after expression construction rewrite the contained `VectorSearchExpression` with them (a tiny `ExpressionRewriter`). Reject both params with 400 if no vector SP is present in the query. Enforce `MaxQueryTextLength` on the value with 400.

Behavior when feature is **disabled** but a vector SP definition exists: parameter resolves as unsupported (same path as unknown parameters) — pin with a test.

- [ ] Steps: failing tests (`?clinicalNote=text` → `VectorSearchExpression`; `:exact` → 400; `:missing=true` → missing expression; `_vectorK=50` flows into expression; `_vectorK` without vector SP → 400) → implement → PASS → commit `feat: parse vector search queries`

### Task 7.3: Query embedding at search time

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs`
- Test: SQL unit tests with substituted `IEmbeddingClient`

In `SearchAsync`, before SQL generation: walk `searchOptions.Expression` for `VectorSearchExpression` nodes (a small visitor); if found, batch-embed their `QueryText` (one Foundry call) and `SetQueryEmbedding`. `EmbeddingUnavailableException` propagates → 503 (ADR §6.2 — never an empty result set).

- [ ] Steps: failing test → implement → PASS → commit `feat: embed query text at search time`

### Task 7.4: SQL generation (ANN-then-filter)

**Files:**
- Create: `Features/Search/Expressions/Visitors/QueryGenerators/VectorSearchParamQueryGenerator.cs`
- Modify: `SqlRootExpressionRewriter.cs`, `SearchParamTableExpressionQueryGeneratorFactory.cs`, `SqlQueryGenerator.cs`
- Test: SQL unit tests asserting generated SQL shape + integration tests

Route `VectorSearchExpression` to a `SearchParamTableExpression` with a new `Kind` handled in `SqlQueryGenerator.VisitTable`. Emitted candidate CTE (parameters via `HashingSqlQueryParameterManager`):

```sql
SELECT TOP (@vectorTopN) ResourceTypeId, ResourceSurrogateId,
       VECTOR_DISTANCE('cosine', Embedding, CAST(@queryEmbedding AS VECTOR(1536))) AS VectorDistance
  FROM dbo.VectorSearchParam
 WHERE SearchParamId = @searchParamId AND EmbeddingModelId = @embeddingModelId
 ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST(@queryEmbedding AS VECTOR(1536)))
```

where `@vectorTopN = (K ?? DefaultK) * OversamplingFactor`. The candidate set joins the rest of the predicate tree exactly like other table expressions; final ordering `ORDER BY VectorDistance ASC, ResourceSurrogateId ASC` (deterministic tiebreak); `MinScore` applied as `WHERE (1 - VectorDistance) >= @minScore`; project `MIN(VectorDistance)` per resource for `perValueRow` chunked rows (any-chunk-matches semantics, best score wins). Authorization/compartment expressions are already part of the predicate tree, so they compose by construction — add a test proving a compartment-restricted vector query excludes out-of-compartment resources.

**Pagination (v1):** continuation token gains an optional `VectorSkip` component; next page re-embeds `QueryText` (one extra Foundry call per page) and skips past `(VectorDistance, ResourceSurrogateId)` of the last row. Stable within a session per ADR §6.5.

- [ ] Steps: failing SQL-shape unit tests → implement generator + rewriter routing → integration tests on vector-capable DB (similarity ordering with stub-deterministic embeddings; hybrid `subject=` filter; `_vectorMinScore` cutoff; pagination) → PASS → commit `feat: ANN-then-filter SQL generation for vector search`

### Task 7.5: Score surfacing

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Search/SearchResultEntry.cs` (add `decimal? Score`)
- Modify: `SqlServerSearchService` (read `VectorDistance` column when present; `Score = 1 - distance`)
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Search/BundleFactory.cs` (set `Bundle.entry.search.score`; mode stays `match`)
- Test: unit tests on both + E2E assertion in Phase 9

- [ ] Steps: failing tests → implement → PASS → commit `feat: surface similarity score in bundle entries`

---

## Phase 8 — Lifecycle & operations

### Task 8.1: `$import` skip + warning

**Files:**
- Modify: the SQL import path (`SqlImporter` / import processing job) to set `SkipVectorEmbedding` on merges and record a per-resource-type warning in the import outcome when a vector SP is registered for an imported type (ADR §3 bulk import: import first, `$reindex` deliberately).
- Test: import integration test asserting no vector rows at import completion and the warning text. Note: in async mode the watchdog *will* see imported transactions and vectorize them on its normal cadence — that is acceptable and strictly better than the sync-mode `$import` story ("import first, reindex deliberately" still applies to sync mode); pin the behavior per mode.

- [ ] Steps: failing test → implement → PASS → commit `feat: $import skips vectorization with warning`

### Task 8.2: Reindex

No new job machinery: `$reindex` re-extracts and merges through the standard path, so in sync mode the enricher embeds (rate-limited by reindex's own throughput controls) and in async mode the reindex merges land in the transaction log and flow through the watchdog → JobQueue pipeline (rate-limited by `JobBatchSize` × drain cadence + the embedding client's rate limit). New vector SPs follow the existing custom-SP status lifecycle (`Supported` → indexed on new writes, not searchable → `Enabled` after reindex), which delivers the ADR's `PendingReindex` gating with zero new states.

- [ ] **Verify with integration tests, not new code:** (a) create vector SP → query returns 400/unsupported until reindex completes; (b) reindex of pre-existing resources populates vector rows in both modes; (c) reindex job telemetry includes embedding call counts (extend the reindex telemetry only if missing).
- [ ] Commit — `test: reindex lifecycle coverage for vector search parameters`

### Task 8.3: Capability statement + docs

- Vector SPs already project into `CapabilityStatement.rest.resource.searchParam` via the standard SP machinery (they are normal `SearchParameter` resources) — pin with a unit test.
- Add `docs/VectorSearch.md`: operator guide — enabling, choosing sync vs async (decision table from ADR §3), the `Prefer: embedding-persistence=synchronous` preference and its `Preference-Applied` signal, model configuration, reindex-after-model-change runbook, monitoring the embedding queue (existing job metrics + `VectorizationLag`), failed-job recovery, and the async-mode `$import` cost behavior (ADR Open Question §10.11).
- Move the ADR out of `Proposals/` (`docs/arch/adr-2605-vector-search-parameter.md`) only after security/compliance review (ADR Consequences) — not part of this plan.

- [ ] Commit — `docs: vector search operator guide`

### Task 8.4: Telemetry wiring

Consolidate metrics behind `IVectorSearchMetricHandler` (pattern: `src/Microsoft.Health.Fhir.Core/Logging/Metrics/Handlers/JobMonitorMetricHandler.cs`): embedding calls/latency/tokens/status (P3), rows produced (P5/P6), `VectorizationLag` + ranges enqueued (P6 — queue depth and failed-job counts come from the existing job-monitoring metrics for QueueType 7), ANN candidate count + post-filter count (P7).

- [ ] Verify all emit points exist; add the missing ones; unit-test handler invocation. Commit — `feat: vector search telemetry`

---

## Phase 9 — End-to-end tests

**Files:** `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/Search/VectorSearchTests.cs` (+ fixture)

Gating: all vector integration/E2E tests run only when the target database reports vector capability (`sys.types` probe) — use a collection fixture that skips otherwise, mirroring existing conditional-skip patterns. CI uses the deterministic in-process embedding stub (hash-of-text → unit vector) so similarity ordering is reproducible; never a live Foundry endpoint.

- [ ] Sync mode: create Observations with notes → vector query returns similarity-ordered results with scores; hybrid `subject=Patient/X` filter; `_count` + continuation; `:missing` both values; `:exact` → 400; transaction bundle of N resources → exactly one embedding client call.
- [ ] Async mode: create → vector query omits the new resource; run watchdog + job → query includes it; update resource → re-vectorized; delete → removed; job re-run → zero embedding calls (idempotent).
- [ ] `Prefer: embedding-persistence=synchronous` in async mode: honored → `Preference-Applied` present + immediately queryable + later job makes no embedding call; client stubbed to fail → write still succeeds, no `Preference-Applied`, resource vectorized by the next watchdog/job pass.
- [ ] Mode switch: write N in async, flip to sync, confirm the watchdog drains the backlog.
- [ ] Suspended kill switch: writes fail 503 in sync mode; writes succeed in async mode while jobs fail-and-retry; vector queries fail 503 in both.
- [ ] Commit — `test: vector search E2E coverage`

---

## Explicitly out of scope (per ADR)

Cosmos DB, attachment decoding, per-SP models, composite/chained vector search, query-embedding cache (ADR Open Question §10.4), failed-job auto-recovery policy (ADR §10.9 — failed jobs are visible via job APIs/metrics only in v1), an `$import` gate for async vectorization (ADR §10.11).

## Standing risks for implementers

1. **Azure SQL vector surface is preview.** Before Phase 4, re-verify `VECTOR` TVP support, `CREATE VECTOR INDEX` syntax, and `VECTOR_DISTANCE` semantics against current Microsoft Learn docs (use the microsoft-docs skill). The TVP `VARCHAR(MAX)`+`CAST` workaround in Task 4.1 is the fallback, not the goal.
2. **`Azure.AI.OpenAI` SDK surface** (Task 3.2): verify exact client/method names against current docs before coding.
3. **Visitor sweeps** (Tasks 2.1, 7.1) are compiler-driven: build the whole solution including Cosmos and test projects; every new abstract/interface member must be implemented everywhere before committing.
4. **MergeResources is the hottest sproc in the system.** The new TVP params must be defaulted and the new inserts must be no-ops when empty; get a perf-conscious review on the 114 diff before merging.
