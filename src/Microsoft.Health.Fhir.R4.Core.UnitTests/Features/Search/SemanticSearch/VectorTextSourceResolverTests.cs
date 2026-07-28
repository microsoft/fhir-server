// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
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
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class VectorTextSourceResolverTests
    {
        private static readonly Uri VectorCanonical = new Uri("https://example.org/fhir/SearchParameter/document-reference-binary-vector");

        [Fact]
        public async Task GivenBinaryInWriteBatch_WhenResolvingReference_ThenBatchTextAndProvenanceAreReturned()
        {
            // Arrange
            IVectorResourceReader resourceReader = Substitute.For<IVectorResourceReader>();
            var resolver = new VectorTextSourceResolver(resourceReader, Deserializers.ResourceDeserializer);
            ResourceWrapper owner = CreateResource("DocumentReference", "document", "1", "{}");
            ResourceWrapper binary = CreateBinary("source", "4", "same-batch text");

            // Act
            IReadOnlyList<VectorTextSource> sources = await resolver.ResolveAsync(
                owner,
                CreateSearchParameter(),
                new[] { "Binary/source" },
                new[] { owner, binary },
                CancellationToken.None);

            // Assert
            VectorTextSource source = Assert.Single(sources);
            Assert.Equal("same-batch text", source.Text);
            Assert.Equal("Binary", source.ResourceType);
            Assert.Equal("source", source.ResourceId);
            Assert.Equal("4", source.ResourceVersion);
            Assert.Equal("Binary.data", source.Path);
            await resourceReader.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        }

        [Fact]
        public async Task GivenPersistedBinary_WhenResolvingReference_ThenReaderTextIsReturned()
        {
            // Arrange
            IVectorResourceReader resourceReader = Substitute.For<IVectorResourceReader>();
            resourceReader.GetAsync(
                    Arg.Is<ResourceKey>(key => key.ResourceType == "Binary" && key.Id == "source"),
                    Arg.Any<CancellationToken>())
                .Returns(CreateBinary("source", "7", "persisted text"));
            var resolver = new VectorTextSourceResolver(resourceReader, Deserializers.ResourceDeserializer);
            ResourceWrapper owner = CreateResource("DocumentReference", "document", "1", "{}");

            // Act
            IReadOnlyList<VectorTextSource> sources = await resolver.ResolveAsync(
                owner,
                CreateSearchParameter(),
                new[] { "Binary/source" },
                new[] { owner },
                CancellationToken.None);

            // Assert
            VectorTextSource source = Assert.Single(sources);
            Assert.Equal("persisted text", source.Text);
            Assert.Equal("7", source.ResourceVersion);
        }

        [Fact]
        public async Task GivenDocumentReferenceToBinary_WhenIndexing_ThenDecodedTextIsEmbeddedWithBinaryProvenance()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateSearchParameter();
            ResourceWrapper owner = CreateResource(
                "DocumentReference",
                "document",
                "2",
                "{}",
                new SearchIndexEntry(searchParameter, new StringSearchValue("Binary/source")));
            ResourceWrapper binary = CreateBinary("source", "5", "decoded clinical passage");
            IVectorSearchParameterResolver searchParameterResolver = Substitute.For<IVectorSearchParameterResolver>();
            searchParameterResolver.GetSearchParameters("DocumentReference").Returns(new[] { searchParameter });
            searchParameterResolver.GetSearchParameters("Binary").Returns(Array.Empty<SearchParameterInfo>());
            var embeddedTexts = new List<string>();
            IEmbeddingClient embeddingClient = Substitute.For<IEmbeddingClient>();
            embeddingClient.Dimensions.Returns(2);
            embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    IReadOnlyList<string> texts = callInfo.ArgAt<IReadOnlyList<string>>(0);
                    embeddedTexts.AddRange(texts);
                    return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.25f, 0.75f }).ToList());
                });
            IEmbeddingModelRegistry modelRegistry = Substitute.For<IEmbeddingModelRegistry>();
            modelRegistry.GetEmbeddingModelIdAsync(Arg.Any<CancellationToken>()).Returns((short)3);
            var configuration = new VectorSearchConfiguration();
            configuration.Indexing.ChunkOverlapTokens = 0;
            var indexer = new VectorSearchIndexer(
                searchParameterResolver,
                new TextChunker(),
                embeddingClient,
                modelRegistry,
                new VectorTextSourceResolver(Substitute.For<IVectorResourceReader>(), Deserializers.ResourceDeserializer),
                Options.Create(configuration));

            // Act
            await indexer.IndexAsync(new[] { owner, binary }, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "decoded clinical passage" }, embeddedTexts);
            VectorSearchChunk chunk = Assert.Single(Assert.Single(owner.VectorSearchIndices).Chunks);
            Assert.Equal("Binary", chunk.SourceResourceType);
            Assert.Equal("source", chunk.SourceResourceId);
            Assert.Equal("5", chunk.SourceResourceVersion);
            Assert.Equal("Binary.data", chunk.SourcePath);
        }

        [Theory]
        [InlineData("application/pdf", "ZmlsZSBjb250ZW50")]
        [InlineData("text/plain; charset=iso-8859-1", "dGV4dA==")]
        [InlineData("text/plain", "not-base64")]
        public async Task GivenUnsupportedBinaryContent_WhenResolvingReference_ThenSourceIsSkipped(string contentType, string data)
        {
            // Arrange
            var resolver = new VectorTextSourceResolver(Substitute.For<IVectorResourceReader>(), Deserializers.ResourceDeserializer);
            ResourceWrapper owner = CreateResource("DocumentReference", "document", "1", "{}");
            ResourceWrapper binary = CreateResource(
                "Binary",
                "source",
                "1",
                $"{{\"resourceType\":\"Binary\",\"id\":\"source\",\"contentType\":\"{contentType}\",\"data\":\"{data}\"}}");

            // Act
            IReadOnlyList<VectorTextSource> sources = await resolver.ResolveAsync(
                owner,
                CreateSearchParameter(),
                new[] { "Binary/source" },
                new[] { owner, binary },
                CancellationToken.None);

            // Assert
            Assert.Empty(sources);
        }

        private static SearchParameterInfo CreateSearchParameter()
        {
            return new SearchParameterInfo(
                name: "DocumentReferenceBinaryVector",
                code: "binary-vector",
                searchParamType: Microsoft.Health.Fhir.ValueSets.SearchParamType.Special,
                url: VectorCanonical,
                expression: "DocumentReference.content.attachment.url",
                baseResourceTypes: new[] { "DocumentReference" },
                vectorConfig: new VectorSearchParameterConfig
                {
                    ExtractionPolicy = VectorTextExtractionPolicy.PerValueRow,
                    SourceStrategy = VectorTextSourceStrategy.LocalBinaryReference,
                },
                definitionStatus: "active");
        }

        private static ResourceWrapper CreateBinary(string id, string version, string text)
        {
            var binary = new Binary
            {
                Id = id,
                ContentType = "text/plain; charset=utf-8",
                Data = Encoding.UTF8.GetBytes(text),
            };

            return CreateResource("Binary", id, version, new FhirJsonSerializer().SerializeToString(binary));
        }

        private static ResourceWrapper CreateResource(
            string resourceType,
            string id,
            string version,
            string rawJson,
            params SearchIndexEntry[] searchIndices)
        {
            return new ResourceWrapper(
                resourceId: id,
                versionId: version,
                resourceTypeName: resourceType,
                rawResource: new RawResource(rawJson, FhirResourceFormat.Json, isMetaSet: true),
                request: new ResourceRequest("POST"),
                lastModified: DateTimeOffset.UtcNow,
                deleted: false,
                searchIndices: searchIndices,
                compartmentIndices: null,
                lastModifiedClaims: Array.Empty<KeyValuePair<string, string>>());
        }
    }
}
