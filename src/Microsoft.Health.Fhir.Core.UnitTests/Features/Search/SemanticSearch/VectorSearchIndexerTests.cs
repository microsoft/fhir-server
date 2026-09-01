// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class VectorSearchIndexerTests
    {
        private static readonly Uri VectorCanonical = new Uri("https://example.org/fhir/SearchParameter/observation-note-vector");
        private static readonly Uri AlternateVectorCanonical = new Uri("https://example.org/fhir/SearchParameter/observation-text-vector");

        [Fact]
        public async Task GivenConcatenatePolicy_WhenIndexingExtractedValues_ThenOnePassageIsEmbeddedWithModelProvenance()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateSearchParameter(VectorTextExtractionPolicy.Concatenate);
            ResourceWrapper resource = CreateResource(
                searchParameter,
                new StringSearchValue("first value"),
                new StringSearchValue("second value"));
            var embeddedTexts = new List<string>();
            VectorSearchIndexer indexer = CreateIndexer(searchParameter, embeddedTexts, embeddingModelId: 7);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "first value\nsecond value" }, embeddedTexts);
            VectorSearchIndexEntry indexEntry = Assert.Single(resource.VectorSearchIndices);
            Assert.Same(searchParameter, indexEntry.SearchParameter);
            Assert.Equal(7, indexEntry.EmbeddingModelId);
            VectorSearchChunk chunk = Assert.Single(indexEntry.Chunks);
            Assert.Equal(0, chunk.ChunkOrdinal);
            Assert.Equal("first value\nsecond value", chunk.ChunkText);
            Assert.Equal(32, chunk.SourceTextHash.Count);
        }

        [Fact]
        public async Task GivenPerValuePolicy_WhenValuesRequireChunking_ThenOrdinalsSpanAllExtractedValues()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateSearchParameter(VectorTextExtractionPolicy.PerValueRow);
            ResourceWrapper resource = CreateResource(
                searchParameter,
                new StringSearchValue("abcdef"),
                new StringSearchValue("gh"));
            var embeddedTexts = new List<string>();
            VectorSearchIndexer indexer = CreateIndexer(searchParameter, embeddedTexts, chunkSize: 4);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "abcd", "ef", "gh" }, embeddedTexts);
            VectorSearchIndexEntry indexEntry = Assert.Single(resource.VectorSearchIndices);
            Assert.Equal(new[] { 0, 1, 2 }, indexEntry.Chunks.Select(chunk => chunk.ChunkOrdinal));
        }

        [Fact]
        public async Task GivenSearchParameterChunkSettings_WhenIndexing_ThenTheyOverrideGlobalDefaults()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateSearchParameter(
                VectorTextExtractionPolicy.Concatenate,
                chunkSizeTokens: 4,
                chunkOverlapTokens: 1);
            ResourceWrapper resource = CreateResource(searchParameter, new StringSearchValue("abcdefghij"));
            var embeddedTexts = new List<string>();
            VectorSearchIndexer indexer = CreateIndexer(searchParameter, embeddedTexts, chunkSize: 10, chunkOverlap: 0);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "abcd", "defg", "ghij" }, embeddedTexts);
        }

        [Fact]
        public async Task GivenNoSearchParameterChunkSettings_WhenIndexing_ThenGlobalDefaultsAreUsed()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateSearchParameter(VectorTextExtractionPolicy.Concatenate);
            ResourceWrapper resource = CreateResource(searchParameter, new StringSearchValue("abcdefghij"));
            var embeddedTexts = new List<string>();
            VectorSearchIndexer indexer = CreateIndexer(searchParameter, embeddedTexts, chunkSize: 5, chunkOverlap: 2);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "abcde", "defgh", "ghij" }, embeddedTexts);
        }

        [Fact]
        public async Task GivenSearchParametersWithDifferentChunkSettings_WhenIndexingOneResource_ThenEachUsesItsOwnSettings()
        {
            // Arrange
            SearchParameterInfo firstSearchParameter = CreateSearchParameter(
                VectorTextExtractionPolicy.Concatenate,
                chunkSizeTokens: 4,
                chunkOverlapTokens: 0);
            SearchParameterInfo secondSearchParameter = CreateSearchParameter(
                VectorTextExtractionPolicy.Concatenate,
                chunkSizeTokens: 5,
                chunkOverlapTokens: 2,
                canonical: AlternateVectorCanonical);
            ResourceWrapper resource = CreateResource(
                new SearchIndexEntry(firstSearchParameter, new StringSearchValue("abcdefgh")),
                new SearchIndexEntry(secondSearchParameter, new StringSearchValue("ijklmnop")));
            var embeddedTexts = new List<string>();
            VectorSearchIndexer indexer = CreateIndexer(
                new[] { firstSearchParameter, secondSearchParameter },
                embeddedTexts,
                chunkSize: 10,
                chunkOverlap: 0);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "abcd", "efgh", "ijklm", "lmnop" }, embeddedTexts);
            Assert.Equal(2, resource.VectorSearchIndices.Count);
        }

        [Fact]
        public async Task GivenResourceWithoutEnabledSearchParameter_WhenIndexing_ThenEmbeddingServiceIsNotCalled()
        {
            // Arrange
            ResourceWrapper resource = CreateResource();
            IVectorSearchParameterResolver resolver = Substitute.For<IVectorSearchParameterResolver>();
            resolver.GetIndexingSearchParameters("Observation").Returns(Array.Empty<SearchParameterInfo>());
            IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();
            var indexer = new VectorSearchIndexer(
                resolver,
                new TextChunker(),
                embeddingClient,
                Substitute.For<IEmbeddingModelRegistry>(),
                CreateTextSourceResolver(),
                Options.Create(CreateConfiguration()),
                NullLogger<VectorSearchIndexer>.Instance);

            // Act
            await indexer.IndexAsync(new[] { resource }, CancellationToken.None);

            // Assert
            Assert.Empty(resource.VectorSearchIndices);
            Assert.True(resource.VectorSearchIndicesUpdated);
            await embeddingClient.DidNotReceiveWithAnyArgs().GenerateEmbeddingsAsync(default, default);
        }

        private static VectorSearchIndexer CreateIndexer(
            SearchParameterInfo searchParameter,
            List<string> embeddedTexts,
            short embeddingModelId = 1,
            int chunkSize = 100,
            int chunkOverlap = 0)
        {
            return CreateIndexer(new[] { searchParameter }, embeddedTexts, embeddingModelId, chunkSize, chunkOverlap);
        }

        private static VectorSearchIndexer CreateIndexer(
            IReadOnlyCollection<SearchParameterInfo> searchParameters,
            List<string> embeddedTexts,
            short embeddingModelId = 1,
            int chunkSize = 100,
            int chunkOverlap = 0)
        {
            IVectorSearchParameterResolver resolver = Substitute.For<IVectorSearchParameterResolver>();
            resolver.GetIndexingSearchParameters("Observation").Returns(searchParameters);

            IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();
            embeddingClient.Dimensions.Returns(2);
            embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    IReadOnlyList<string> texts = callInfo.ArgAt<IReadOnlyList<string>>(0);
                    embeddedTexts.AddRange(texts);
                    IReadOnlyList<float[]> embeddings = texts.Select(_ => new[] { 0.25f, 0.75f }).ToList();
                    return Task.FromResult(embeddings);
                });

            IEmbeddingModelRegistry embeddingModelRegistry = Substitute.For<IEmbeddingModelRegistry>();
            embeddingModelRegistry.GetEmbeddingModelIdAsync(Arg.Any<CancellationToken>()).Returns(embeddingModelId);

            VectorSearchConfiguration configuration = CreateConfiguration();
            configuration.Indexing.ChunkSizeTokens = chunkSize;
            configuration.Indexing.ChunkOverlapTokens = chunkOverlap;

            return new VectorSearchIndexer(
                resolver,
                new TextChunker(),
                embeddingClient,
                embeddingModelRegistry,
                CreateTextSourceResolver(),
                Options.Create(configuration),
                NullLogger<VectorSearchIndexer>.Instance);
        }

        private static VectorTextSourceResolver CreateTextSourceResolver()
        {
            return new VectorTextSourceResolver(
                Substitute.For<IVectorResourceReader>(),
                Substitute.For<IResourceDeserializer>(),
                new[] { new PlainTextBinaryContentExtractor() });
        }

        private static VectorSearchConfiguration CreateConfiguration()
        {
            return new VectorSearchConfiguration();
        }

        private static SearchParameterInfo CreateSearchParameter(
            VectorTextExtractionPolicy extractionPolicy,
            int? chunkSizeTokens = null,
            int? chunkOverlapTokens = null,
            Uri canonical = null)
        {
            return new SearchParameterInfo(
                name: "ObservationNoteVector",
                code: "note-vector",
                searchParamType: SearchParamType.Special,
                url: canonical ?? VectorCanonical,
                expression: "Observation.note.text",
                baseResourceTypes: new[] { "Observation" },
                vectorConfig: new VectorSearchParameterConfig
                {
                    ExtractionPolicy = extractionPolicy,
                    ChunkSizeTokens = chunkSizeTokens,
                    ChunkOverlapTokens = chunkOverlapTokens,
                },
                definitionStatus: "active");
        }

        private static ResourceWrapper CreateResource(
            SearchParameterInfo searchParameter = null,
            params StringSearchValue[] values)
        {
            IReadOnlyCollection<SearchIndexEntry> searchIndices = searchParameter == null
                ? Array.Empty<SearchIndexEntry>()
                : values.Select(value => new SearchIndexEntry(searchParameter, value)).ToList();

            return CreateResource(searchIndices);
        }

        private static ResourceWrapper CreateResource(params SearchIndexEntry[] searchIndices)
        {
            return CreateResource((IReadOnlyCollection<SearchIndexEntry>)searchIndices);
        }

        private static ResourceWrapper CreateResource(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            return new ResourceWrapper(
                resourceId: "example",
                versionId: "1",
                resourceTypeName: "Observation",
                rawResource: new RawResource("{}", FhirResourceFormat.Json, isMetaSet: true),
                request: new ResourceRequest("POST"),
                lastModified: DateTimeOffset.UtcNow,
                deleted: false,
                searchIndices: searchIndices,
                compartmentIndices: null,
                lastModifiedClaims: Array.Empty<KeyValuePair<string, string>>());
        }
    }
}
