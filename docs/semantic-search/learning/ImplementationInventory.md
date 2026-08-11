# SQL Semantic Search: Implementation Inventory

This document accounts for every file changed in commit `286cb45a3`. It is a
historical implementation checkpoint, not an inventory of later schema 118,
reindex, PDF, sorting, pagination, MCP, or demo-agent work. Learn the critical
files first, then use the exhaustive inventory to answer “where did the SQL
integration begin?” questions.

## How To Read This Document

Change types:

- **A**: added by the commit;
- **M**: an existing file was modified;
- **D**: deleted because its responsibility moved.

Study priorities:

- **Own**: understand the control flow and be able to open the relevant code.
- **Recognize**: know why the file exists and what it connects.
- **Evidence**: know the behavior the test proves; do not memorize the test.
- **Mechanical**: project inclusion, schema support, or small compatibility work.

## The 20 Files To Study Closely

Read these in order. The line anchors point to the most useful current code.

| # | File and anchor | What you should understand |
|---:|---|---|
| 1 | [VectorSearchConfiguration.Validate](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchConfiguration.cs#L50) | Feature invariants: model, dimensions, synchronous indexing, chunk limits, candidate limits, extraction bounds, and cosine metric. |
| 2 | [VectorSearchParameterConfig](../../../src/Microsoft.Health.Fhir.Core/Models/VectorSearchParameterConfig.cs#L11) | The three per-SearchParameter controls: extraction policy, source strategy, max input tokens. |
| 3 | [SearchParameterWrapper.ParseVectorConfig](../../../src/Microsoft.Health.Fhir.Core/Features/Definition/BundleWrappers/SearchParameterWrapper.cs#L75) | How the FHIR extension becomes typed server configuration and how invalid definitions are rejected. |
| 4 | [VectorSearchParameterResolver](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchParameterResolver.cs#L21) | How eligible vector SearchParameters are discovered from the live registry by resource type and canonical URL. |
| 5 | [VectorSearchIndexer.IndexAsync](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchIndexer.cs#L56) | Complete write-time orchestration: values, sources, policies, chunks, one embedding batch, hashes, provenance. |
| 6 | [VectorTextSourceResolver.ResolveAsync](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorTextSourceResolver.cs#L44) | Direct text versus local Binary lookup and MIME-aware extraction for strict UTF-8 text or page-scoped PDF text. |
| 7 | [ResourceWrapper](../../../src/Microsoft.Health.Fhir.Core/Features/Persistence/ResourceWrapper.cs) | Where generated `VectorSearchIndexEntry` objects ride with the resource into SQL persistence. |
| 8 | [SqlServerFhirDataStore indexing call](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs#L460) | Why indexing is on the SQL write path and occurs before merge. |
| 9 | [VectorSearchParamListRowGenerator](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/VectorSearchParamListRowGenerator.cs#L17) | Translation from Core chunk objects to schema-generated SQL TVP rows. |
| 10 | [VectorSearchParam.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/VectorSearchParam.sql#L1) | Owner identity, SearchParameter/model identity, passage text/hash, provenance, and `vector(1536)`. |
| 11 | [SearchParameterExpressionParser](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/Parsers/SearchParameterExpressionParser.cs) | How `type=special` vector parameters enter the normal FHIR expression tree. |
| 12 | [VectorSearchQueryProcessor.PrepareAsync](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchQueryProcessor.cs#L38) | At most one vector parameter, query embedding generation, dimension validation, model ID. |
| 13 | [SqlServerSearchService](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs#L188) | Ordinary resource-level integration: prepare vector, remove vector node, run structured SQL, read score/evidence. |
| 14 | [SqlQueryGenerator.AppendVectorSearchApply](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SqlQueryGenerator.cs#L454) | Correlated `CROSS APPLY`, `TOP (1)` best chunk, `VECTOR_DISTANCE`, model/SearchParameter constraints. |
| 15 | [SemanticSearchController.Search](../../../src/Microsoft.Health.Fhir.Shared.Api/Controllers/SemanticSearchController.cs#L61) | Custom Patient operation contract and request validation. |
| 16 | [SemanticSearchHandler.Handle](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Search/SemanticSearch/SemanticSearchHandler.cs#L66) | Read authorization, patient-scoped candidates, data filtering, global result Bundle. |
| 17 | [SqlDocumentReferenceSemanticSearch.SearchAsync](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlDocumentReferenceSemanticSearch.cs#L52) | Embed once, group candidates by type, rank each enabled parameter, de-duplicate and globally sort. |
| 18 | [SqlVectorStore](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlVectorStore.cs) | Direct candidate-ID vector ranking and conversion from distance to hit/evidence data. |
| 19 | [BundleFactory evidence](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Search/BundleFactory.cs#L47) | How score and semantic evidence are represented in a FHIR search Bundle. |
| 20 | [Startup.AddSemanticSearch](../../../src/Microsoft.Health.Fhir.Shared.Web/Startup.cs#L133) | Why the feature is SQL-only, opt-in, and how every service is wired. |

## Data Objects To Know

You should be able to describe these without reading their constructors:

| Object | Meaning |
|---|---|
| `SearchParameterInfo.VectorConfig` | Parsed vector metadata attached to a registered FHIR SearchParameter. |
| `VectorTextSource` | Resolved text plus source resource type, ID, version, and path. |
| `VectorSearchChunk` | Indexed passage plus ordinal, hash, embedding, and source provenance. |
| `VectorSearchIndexEntry` | All chunks for one owner resource, SearchParameter, and embedding model. |
| `PreparedVectorSearchQuery` | One query embedding plus the selected SearchParameter and model ID. |
| `VectorSearchHit` | SQL-ranked winning chunk and source data for an owner resource. |
| `VectorSearchResult` | Resource type + surrogate ID + score + evidence, suitable for mixed ranking. |
| `SemanticSearchEvidence` | Exact passage and provenance exposed in the FHIR Bundle. |

## Exhaustive Inventory: Build And Azure Integration

| Change | Priority | File | Why it changed |
|---|---|---|---|
| M | Mechanical | [Directory.Packages.props](../../../Directory.Packages.props) | Pins a preview build-time code generator containing vector schema-model support needed by the SQL schema changes. |
| M | Recognize | [AzureFoundryEmbeddingClient.cs](../../../src/Microsoft.Health.Fhir.Azure/SemanticSearch/AzureFoundryEmbeddingClient.cs) | Uses the new Core-owned `VectorSearchEmbeddingConfiguration`; remains the production `IEmbeddingClient`. |
| M | Evidence | [AzureFoundryEmbeddingClientTests.cs](../../../src/Microsoft.Health.Fhir.Azure.UnitTests/SemanticSearch/AzureFoundryEmbeddingClientTests.cs) | Updates Azure embedding-client tests to construct the new Core configuration type. |
| D | Recognize | `src/Microsoft.Health.Fhir.Azure/SemanticSearch/EmbeddingConfiguration.cs` | Deleted because embedding configuration moved from Azure infrastructure into Core feature configuration. |

## Exhaustive Inventory: Core Configuration

| Change | Priority | File | Why it changed |
|---|---|---|---|
| M | Recognize | [CoreFeatureConfiguration.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/CoreFeatureConfiguration.cs) | Adds the root `VectorSearch` feature settings to existing FHIR Core configuration. |
| A | Own | [VectorSearchConfiguration.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchConfiguration.cs) | Defines root vector settings, supported SQL dimensions/metric, and startup validation. |
| A | Recognize | [VectorSearchEmbeddingConfiguration.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchEmbeddingConfiguration.cs) | Holds endpoint, deployment, model identity, and dimensions. |
| A | Recognize | [VectorSearchIndexingConfiguration.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchIndexingConfiguration.cs) | Holds indexing mode, default chunk size, and default overlap. |
| A | Recognize | [VectorSearchIndexingMode.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchIndexingMode.cs) | Makes the current synchronous mode explicit and leaves room for a later asynchronous mode. |
| A | Recognize | [VectorSearchQueryConfiguration.cs](../../../src/Microsoft.Health.Fhir.Core/Configs/VectorSearchQueryConfiguration.cs) | Holds default/max result counts, patient-operation candidate count, and distance metric. |
| A | Evidence | [VectorSearchConfigurationTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Config/VectorSearchConfigurationTests.cs) | Proves disabled configuration is inert and enabled configuration rejects incompatible/missing values. |

## Exhaustive Inventory: SearchParameter Definition And Models

| Change | Priority | File | Why it changed |
|---|---|---|---|
| M | Own | [SearchParameterWrapper.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Definition/BundleWrappers/SearchParameterWrapper.cs) | Reads SearchParameter status and nested vector configuration extension into typed metadata. |
| M | Recognize | [SearchParameterInfo.cs](../../../src/Microsoft.Health.Fhir.Core/Models/SearchParameterInfo.cs) | Carries publication status and optional `VectorConfig` in the normal SearchParameter model. |
| A | Own | [VectorSearchParameterConfig.cs](../../../src/Microsoft.Health.Fhir.Core/Models/VectorSearchParameterConfig.cs) | Defines extension URLs, extraction policy, source strategy, and input limit defaults. |
| A | Recognize | [VectorTextExtractionPolicy.cs](../../../src/Microsoft.Health.Fhir.Core/Models/VectorTextExtractionPolicy.cs) | Defines `FirstValue`, `Concatenate`, and `PerValueRow` behavior. |
| A | Recognize | [VectorTextSourceStrategy.cs](../../../src/Microsoft.Health.Fhir.Core/Models/VectorTextSourceStrategy.cs) | Defines `DirectText` and `LocalBinaryReference`. |
| M | Recognize | [SearchParameterInfoExtensions.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SearchParameterInfoExtensions.cs) | Includes vector metadata in the SearchParameter hash so indexing changes invalidate stale definitions. |
| M | Evidence | [SearchParameterInfoExtensionsTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SearchParameterInfoExtensionsTests.cs) | Proves different vector configurations produce different hashes. |
| M | Evidence | [SearchParameterDefinitionBuilderTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Definition/SearchParameterDefinitionBuilderTests.cs) | Proves valid vector extensions are parsed and invalid extraction configuration is rejected. |

## Exhaustive Inventory: Search Expression Integration

| Change | Priority | File | Why it changed |
|---|---|---|---|
| A | Own | [VectorSearchExpression.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/VectorSearchExpression.cs) | Adds an expression-tree node carrying the vector SearchParameter and natural-language query. |
| M | Recognize | [IExpressionVisitor.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/IExpressionVisitor.cs) | Adds visitor dispatch for `VectorSearchExpression`. |
| M | Mechanical | [DefaultExpressionVisitor.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/DefaultExpressionVisitor.cs) | Supplies default handling so existing visitors remain compatible. |
| M | Mechanical | [ExpressionRewriter.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/ExpressionRewriter.cs) | Preserves vector nodes through generic expression rewriting unless a specialized rewriter removes them. |
| M | Own | [SearchParameterExpressionParser.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/Expressions/Parsers/SearchParameterExpressionParser.cs) | Recognizes a configured `special` parameter and emits `VectorSearchExpression` instead of ordinary matching semantics. |
| A | Recognize | [RemoveVectorSearchRewriter.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/RemoveVectorSearchRewriter.cs) | Removes vector nodes after query preparation so ordinary SQL expressions only build deterministic candidate predicates. |
| M | Recognize | [SearchParamTableExpressionQueryGeneratorFactory.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/SearchParamTableExpressionQueryGeneratorFactory.cs) | Guards against sending vector expressions through the wrong standard search-table generator. |
| M | Recognize | [TopRewriter.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/TopRewriter.cs) | Avoids applying the ordinary early `TOP` optimization before semantic ranking. |
| A | Evidence | [VectorSearchExpressionParserTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Search/Expressions/Parsers/VectorSearchExpressionParserTests.cs) | Proves parsing, feature gating, and vector-parameter validation. |
| A | Evidence | [RemoveVectorSearchRewriterTests.cs](../../../src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/Expressions/RemoveVectorSearchRewriterTests.cs) | Proves vector nodes are removed while structured expressions remain. |
| M | Evidence | [TopRewriterTests.cs](../../../src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/Expressions/Visitors/TopRewriterTests.cs) | Proves structured candidates are not prematurely limited for vector ranking. |

## Exhaustive Inventory: Core Indexing And Query Services

| Change | Priority | File | Why it changed |
|---|---|---|---|
| A | Recognize | [IVectorSearchIndexer.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorSearchIndexer.cs) | Defines write-time indexing without coupling Core to SQL. |
| A | Own | [VectorSearchIndexer.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchIndexer.cs) | Orchestrates extraction, source resolution, policies, chunking, batch embedding, hashing, and index-entry creation. |
| A | Recognize | [IVectorSearchParameterResolver.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorSearchParameterResolver.cs) | Defines lookup of enabled vector SearchParameters by resource type/canonical. |
| A | Own | [VectorSearchParameterResolver.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchParameterResolver.cs) | Resolves and validates enabled canonical URLs against the FHIR definition manager. |
| A | Recognize | [IVectorTextSourceResolver.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorTextSourceResolver.cs) | Abstracts direct and linked text-source resolution. |
| A | Own | [VectorTextSourceResolver.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorTextSourceResolver.cs) | Implements direct text and strict local Binary reference decoding with provenance. |
| A | Recognize | [IVectorResourceReader.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorResourceReader.cs) | Narrow read contract for linked resources, avoiding a circular dependency on the full data store. |
| A | Recognize | [IVectorSearchQueryProcessor.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorSearchQueryProcessor.cs) | Defines conversion from expression tree to prepared query vector. |
| A | Own | [VectorSearchQueryProcessor.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchQueryProcessor.cs) | Finds the one allowed vector expression, embeds it, validates dimensions, and records model identity. |
| A | Recognize | [IEmbeddingModelRegistry.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IEmbeddingModelRegistry.cs) | Abstracts stable SQL model identity from Core query/indexing logic. |
| M | Recognize | [IVectorStore.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IVectorStore.cs) | Expands the vector ranking contract to include SearchParameter/model/metric and return passage provenance. |
| M | Mechanical | [DeterministicEmbeddingClient.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/DeterministicEmbeddingClient.cs) | Aligns deterministic test embeddings with the supported 1536-dimensional configuration. |

## Exhaustive Inventory: Core Data Transfer And Evidence Models

| Change | Priority | File | Why it changed |
|---|---|---|---|
| A | Recognize | [VectorTextSource.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorTextSource.cs) | Carries resolved text and its source provenance before chunking. |
| M | Recognize | [VectorSearchChunk.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchChunk.cs) | Adds exact chunk text and source resource/path provenance to the stored passage model. |
| A | Recognize | [VectorSearchIndexEntry.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchIndexEntry.cs) | Groups chunks by owner SearchParameter and embedding model for persistence. |
| A | Recognize | [PreparedVectorSearchQuery.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/PreparedVectorSearchQuery.cs) | Carries query vector, SearchParameter, and model ID into SQL generation. |
| A | Recognize | [VectorSearchHit.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchHit.cs) | Carries SQL-ranked owner ID, score, winning passage, and source provenance. |
| M | Recognize | [VectorSearchResult.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/VectorSearchResult.cs) | Adds resource type so mixed results use `(type, surrogate ID)` identity. |
| A | Own | [SemanticSearchEvidence.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/SemanticSearchEvidence.cs) | Defines the evidence extension contract: text, ordinal, canonical, source, and path. |
| M | Recognize | [ResourceWrapper.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Persistence/ResourceWrapper.cs) | Adds in-memory vector indices to the resource object passed through SQL merge. |
| M | Recognize | [SearchResultEntry.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SearchResultEntry.cs) | Adds optional semantic score and evidence to ordinary search results. |
| A | Evidence | [SemanticSearchEvidenceTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SemanticSearch/SemanticSearchEvidenceTests.cs) | Proves evidence validation and property mapping. |
| A | Evidence | [VectorSearchIndexerTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SemanticSearch/VectorSearchIndexerTests.cs) | Proves extraction-policy handling, embedding batching, chunk metadata, and indexing behavior. |
| A | Evidence | [VectorSearchParameterResolverTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SemanticSearch/VectorSearchParameterResolverTests.cs) | Proves allow-list/resource-type resolution and invalid-definition handling. |
| A | Evidence | [VectorSearchQueryProcessorTests.cs](../../../src/Microsoft.Health.Fhir.Core.UnitTests/Features/Search/SemanticSearch/VectorSearchQueryProcessorTests.cs) | Proves zero/one/multiple vector-expression behavior and vector validation. |
| A | Evidence | [VectorTextSourceResolverTests.cs](../../../src/Microsoft.Health.Fhir.R4.Core.UnitTests/Features/Search/SemanticSearch/VectorTextSourceResolverTests.cs) | Proves direct text, same-batch/persisted Binary lookup, MIME/UTF-8 limits, and provenance. |

## Exhaustive Inventory: Patient Operation And Bundle Output

| Change | Priority | File | Why it changed |
|---|---|---|---|
| A | Recognize | [SemanticSearchRequest.cs](../../../src/Microsoft.Health.Fhir.Core/Messages/SemanticSearch/SemanticSearchRequest.cs) | Defines query, patient reference, count, and optional resource-type selection for MediatR. |
| A | Recognize | [SemanticSearchResponse.cs](../../../src/Microsoft.Health.Fhir.Core/Messages/SemanticSearch/SemanticSearchResponse.cs) | Carries the resulting FHIR Bundle resource element. |
| M | Recognize | [KnownRoutes.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Routing/KnownRoutes.cs) | Adds the `Patient/{id}/$semantic-search` route template. |
| A | Own | [SemanticSearchController.cs](../../../src/Microsoft.Health.Fhir.Shared.Api/Controllers/SemanticSearchController.cs) | Implements HTTP/FHIR Parameters validation for the custom operation. |
| A | Own | [SemanticSearchHandler.cs](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Search/SemanticSearch/SemanticSearchHandler.cs) | Applies authorization and patient/type candidate search before global semantic ranking and Bundle assembly. |
| A | Recognize | [IDocumentReferenceSemanticSearch.cs](../../../src/Microsoft.Health.Fhir.Core/Features/Search/SemanticSearch/IDocumentReferenceSemanticSearch.cs) | Defines candidate-list semantic ranking; name is now narrower than its mixed-resource implementation. |
| M | Own | [BundleFactory.cs](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Search/BundleFactory.cs) | Emits `search.score` and a consistent semantic evidence extension for ordinary semantic results. |
| A | Evidence | [SemanticSearchControllerTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Controllers/SemanticSearchControllerTests.cs) | Proves route-derived patient, request validation, defaults, repeated types, and unsupported-type rejection. |
| A | Evidence | [SemanticSearchHandlerTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Search/SemanticSearch/SemanticSearchHandlerTests.cs) | Proves three-type candidate search, patient constraint, global order, type filter, and Bundle evidence. |
| M | Evidence | [BundleFactoryTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Search/BundleFactoryTests.cs) | Proves ordinary Bundle entries preserve semantic score and evidence fields. |

## Exhaustive Inventory: API, Startup, And Project Inclusion

| Change | Priority | File | Why it changed |
|---|---|---|---|
| M | Own | [FhirServerServiceCollectionExtensions.cs](../../../src/Microsoft.Health.Fhir.Shared.Api/Registration/FhirServerServiceCollectionExtensions.cs) | Validates vector configuration and exposes it through options during server registration. |
| M | Own | [Startup.cs](../../../src/Microsoft.Health.Fhir.Shared.Web/Startup.cs) | Conditionally registers the SQL semantic service graph and Azure Foundry client only when enabled. |
| M | Recognize | [appsettings.json](../../../src/Microsoft.Health.Fhir.Shared.Web/appsettings.json) | Documents disabled-by-default configuration shape and operational defaults. |
| A | Evidence | [FhirServerServiceCollectionExtensionsTests.cs](../../../src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Registration/FhirServerServiceCollectionExtensionsTests.cs) | Proves configuration validation/options registration behavior. |
| M | Mechanical | [Microsoft.Health.Fhir.Shared.Api.projitems](../../../src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems) | Includes the new controller in all shared API target projects. |
| M | Mechanical | [Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems](../../../src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Microsoft.Health.Fhir.Shared.Api.UnitTests.projitems) | Includes new API controller/registration tests. |
| M | Mechanical | [Microsoft.Health.Fhir.Shared.Core.projitems](../../../src/Microsoft.Health.Fhir.Shared.Core/Microsoft.Health.Fhir.Shared.Core.projitems) | Includes the semantic handler in all shared Core target projects. |
| M | Mechanical | [Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems](../../../src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Microsoft.Health.Fhir.Shared.Core.UnitTests.projitems) | Includes new shared Core semantic tests. |
| M | Mechanical | [CreateResourceHandler.cs](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Create/CreateResourceHandler.cs) | Adds a missing Core features namespace import and formatting cleanup; no new semantic control flow. |
| M | Mechanical | [UpsertResourceHandler.cs](../../../src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Upsert/UpsertResourceHandler.cs) | Adds the same Core features namespace import and formatting cleanup; no new semantic control flow. |

## Exhaustive Inventory: SQL Schema And Transactional Persistence

| Change | Priority | File | Why it changed |
|---|---|---|---|
| M | Own | [117.diff.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/117.diff.sql) | Deploys model/passages/TVP objects and updates resource merge procedures for vector rows. |
| M | Recognize | [EmbeddingModel.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/EmbeddingModel.sql) | Defines stable model/version metadata and model ID. |
| A | Own | [VectorSearchParam.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/VectorSearchParam.sql) | Defines persisted passages, provenance, hashes, and native vectors. |
| A | Recognize | [VectorSearchParamList.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Types/VectorSearchParamList.sql) | Defines the table-valued parameter shape for batch persistence. |
| M | Recognize | [MergeResources.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResources.sql) | Deletes/replaces vector rows transactionally with current resource versions. |
| M | Mechanical | [MergeResourcesAndSearchParams.sql](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResourcesAndSearchParams.sql) | Forwards the new vector TVP through the higher-level merge procedure. |
| A | Own | [VectorSearchParamListRowGenerator.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/VectorSearchParamListRowGenerator.cs) | Maps owner IDs, SearchParameter IDs, chunks, provenance, and formatted embeddings to TVP rows. |
| M | Own | [SqlServerFhirDataStore.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs) | Calls indexing before merge and adds `@VectorSearchParams` to resource persistence. |
| M | Recognize | [TransactionWatchdog.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/TransactionWatchdog.cs) | Indexes resources recovered from timed-out transactions before the watchdog commits them. |
| M | Mechanical | [VectorSearchParam.sql tool copy](../../../tools/SemanticSearch/VectorSearchParam.sql) | Keeps the standalone development SQL helper aligned by adding stored chunk text. |

## Exhaustive Inventory: SQL Query And Ranking

| Change | Priority | File | Why it changed |
|---|---|---|---|
| A | Recognize | [SqlVectorFormatter.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlVectorFormatter.cs) | Formats `float[]` values for SQL native vector parameters. |
| A | Recognize | [SqlEmbeddingModelRegistry.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlEmbeddingModelRegistry.cs) | Creates or retrieves the configured embedding model ID in SQL. |
| A | Recognize | [SqlVectorResourceReader.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlVectorResourceReader.cs) | Reads a current local Binary directly from SQL for linked-source indexing. |
| M | Own | [SqlVectorStore.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlVectorStore.cs) | Ranks candidate owner IDs, returns winning chunks/scores, and projects source provenance. |
| A | Own | [SqlDocumentReferenceSemanticSearch.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SemanticSearch/SqlDocumentReferenceSemanticSearch.cs) | Performs mixed-type ranking for the Patient operation despite its legacy class name. |
| M | Recognize | [SqlSearchOptions.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchOptions.cs) | Carries the prepared vector query beside ordinary search options. |
| M | Own | [SqlServerSearchService.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlServerSearchService.cs) | Integrates vector preparation, structured expression rewrite, semantic columns, score, and evidence into normal SQL search. |
| M | Own | [SqlQueryGenerator.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SqlQueryGenerator.cs) | Generates best-chunk `VECTOR_DISTANCE` SQL and semantic-first ordering after structured filtering. |
| M | Recognize | [FhirServerBuilderSqlServerRegistrationExtensions.cs](../../../src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs) | Registers `SqlVectorResourceReader` through the existing SQL dependency-injection builder. |
| M | Evidence | [SqlVectorStoreTests.cs](../../../src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SemanticSearch/SqlVectorStoreTests.cs) | Exercises SQL storage/ranking and verifies Binary source provenance when a test database is configured. |
| M | Evidence | [SqlQueryGeneratorTests.cs](../../../src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Search/SqlQueryGeneratorTests.cs) | Proves generated SQL ranks structured candidates by vector distance before limiting output. |
| M | Evidence | [SqlServerFhirDataStoreUnitTests.cs](../../../src/Microsoft.Health.Fhir.SqlServer.UnitTests/Features/Storage/SqlServerFhirDataStoreUnitTests.cs) | Proves vector TVP generation preserves model, text, hash, embedding, and provenance metadata. |

## Count Check

The tables above account for all 98 committed paths:

| Area | Files |
|---|---:|
| Build and Azure integration | 4 |
| Core configuration | 7 |
| SearchParameter definition and models | 8 |
| Search expression integration | 11 |
| Core indexing and query services | 12 |
| Core DTO and evidence models | 14 |
| Patient operation and Bundle output | 10 |
| API, startup, and project inclusion | 10 |
| SQL schema and persistence | 10 |
| SQL query and ranking | 12 |
| **Total** | **98** |

## Files Deliberately Not In The Commit

These current worktree items are not implementation evidence for commit
`286cb45a3`:

- the four uncommitted `SemanticSearch*` HTTP E2E files under the R4 E2E test
  project;
- `tools/SemanticSearchDemo-July20/`;
- `tools/SyntheticDocumentReferenceData/`;
- `tools/SemanticSearch/Run-EmbeddingDemo.ps1`;
- machine-local NuGet feed and warning-suppression edits;
- `_review_diff.txt`.

Keep that boundary clear when presenting “what was implemented” versus “what is
being prepared for the demo.”