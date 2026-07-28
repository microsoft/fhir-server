// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
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
    public sealed class VectorSearchQueryProcessorTests
    {
        private static readonly Uri VectorCanonical = new Uri("https://example.org/fhir/SearchParameter/semantic-text");
        private readonly IEmbeddingClient _embeddingClient = Substitute.For<IEmbeddingClient>();
        private readonly IEmbeddingModelRegistry _embeddingModelRegistry = Substitute.For<IEmbeddingModelRegistry>();

        public VectorSearchQueryProcessorTests()
        {
            _embeddingClient.Dimensions.Returns(VectorSearchConfiguration.SupportedDimensions);
        }

        [Fact]
        public async Task GivenNoVectorExpression_WhenPreparing_ThenNoEmbeddingIsGenerated()
        {
            // Arrange
            VectorSearchQueryProcessor processor = CreateProcessor();
            Expression expression = Expression.StringEquals(FieldName.String, null, "ordinary value", false);

            // Act
            PreparedVectorSearchQuery result = await processor.PrepareAsync(expression, CancellationToken.None);

            // Assert
            Assert.Null(result);
            await _embeddingClient.DidNotReceiveWithAnyArgs().GenerateEmbeddingsAsync(default, default);
            await _embeddingModelRegistry.DidNotReceiveWithAnyArgs().GetEmbeddingModelIdAsync(default);
        }

        [Fact]
        public async Task GivenOneVectorExpression_WhenPreparing_ThenEmbeddingAndModelProvenanceAreReturned()
        {
            // Arrange
            const string queryText = "breathing difficulty overnight";
            const short embeddingModelId = 7;
            using var cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            float[] embedding = Enumerable.Repeat(0.25f, VectorSearchConfiguration.SupportedDimensions).ToArray();
            SearchParameterInfo searchParameter = CreateSearchParameter();
            _embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), cancellationToken)
                .Returns(Task.FromResult<IReadOnlyList<float[]>>(new[] { embedding }));
            _embeddingModelRegistry.GetEmbeddingModelIdAsync(cancellationToken).Returns(embeddingModelId);
            VectorSearchQueryProcessor processor = CreateProcessor();

            // Act
            PreparedVectorSearchQuery result = await processor.PrepareAsync(
                new VectorSearchExpression(searchParameter, queryText),
                cancellationToken);
            embedding[0] = 1.0f;

            // Assert
            Assert.Same(searchParameter, result.SearchParameter);
            Assert.Equal(embeddingModelId, result.EmbeddingModelId);
            Assert.Equal(0.25f, result.Embedding[0]);
            Assert.Equal(VectorSearchConfiguration.SupportedDimensions, result.Embedding.Count);
            await _embeddingClient.Received(1).GenerateEmbeddingsAsync(
                Arg.Is<IReadOnlyList<string>>(texts => texts.SequenceEqual(new[] { queryText })),
                cancellationToken);
            await _embeddingModelRegistry.Received(1).GetEmbeddingModelIdAsync(cancellationToken);
        }

        [Fact]
        public async Task GivenMultipleVectorExpressions_WhenPreparing_ThenSearchIsRejectedBeforeEmbedding()
        {
            // Arrange
            VectorSearchQueryProcessor processor = CreateProcessor();
            SearchParameterInfo searchParameter = CreateSearchParameter();
            Expression expression = Expression.And(
                new VectorSearchExpression(searchParameter, "first query"),
                new VectorSearchExpression(searchParameter, "second query"));

            // Act
            Func<Task> prepare = () => processor.PrepareAsync(expression, CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<InvalidSearchOperationException>(prepare);
            await _embeddingClient.DidNotReceiveWithAnyArgs().GenerateEmbeddingsAsync(default, default);
        }

        [Fact]
        public async Task GivenEmbeddingServiceReturnsWrongCount_WhenPreparing_ThenPreparationFails()
        {
            // Arrange
            _embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<float[]>>(Array.Empty<float[]>()));
            VectorSearchQueryProcessor processor = CreateProcessor();

            // Act
            Func<Task> prepare = () => processor.PrepareAsync(CreateVectorExpression(), CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(prepare);
            await _embeddingModelRegistry.DidNotReceiveWithAnyArgs().GetEmbeddingModelIdAsync(default);
        }

        [Fact]
        public async Task GivenEmbeddingServiceReturnsWrongDimensions_WhenPreparing_ThenPreparationFails()
        {
            // Arrange
            var embedding = new float[VectorSearchConfiguration.SupportedDimensions - 1];
            _embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<float[]>>(new[] { embedding }));
            VectorSearchQueryProcessor processor = CreateProcessor();

            // Act
            Func<Task> prepare = () => processor.PrepareAsync(CreateVectorExpression(), CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(prepare);
            await _embeddingModelRegistry.DidNotReceiveWithAnyArgs().GetEmbeddingModelIdAsync(default);
        }

        private VectorSearchQueryProcessor CreateProcessor()
        {
            return new VectorSearchQueryProcessor(_embeddingClient, _embeddingModelRegistry);
        }

        private static VectorSearchExpression CreateVectorExpression()
        {
            return new VectorSearchExpression(CreateSearchParameter(), "breathing difficulty");
        }

        private static SearchParameterInfo CreateSearchParameter()
        {
            return new SearchParameterInfo(
                name: "SemanticText",
                code: "semantic-text",
                searchParamType: SearchParamType.Special,
                url: VectorCanonical,
                expression: "Resource.text.div",
                baseResourceTypes: new[] { "Resource" },
                vectorConfig: new VectorSearchParameterConfig(),
                definitionStatus: "active");
        }
    }
}
