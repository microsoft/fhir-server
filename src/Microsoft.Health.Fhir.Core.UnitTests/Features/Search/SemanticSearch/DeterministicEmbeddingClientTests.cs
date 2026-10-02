// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DeterministicEmbeddingClientTests
    {
        private readonly DeterministicEmbeddingClient _client = new DeterministicEmbeddingClient(dimensions: 1536);

        [Fact]
        public async Task GivenTheSameText_WhenEmbeddedTwice_ThenTheVectorsAreIdentical()
        {
            var first = await _client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);
            var second = await _client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);

            Assert.Equal(first[0], second[0]);
        }

        [Fact]
        public async Task GivenText_WhenEmbedded_ThenTheVectorHasTheConfiguredDimensions()
        {
            var embeddings = await _client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);

            Assert.Equal(1536, embeddings[0].Length);
        }

        [Fact]
        public async Task GivenText_WhenEmbedded_ThenTheVectorIsL2Normalized()
        {
            var embeddings = await _client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);

            double magnitude = Math.Sqrt(embeddings[0].Sum(component => (double)component * component));

            Assert.Equal(1.0, magnitude, precision: 4);
        }

        [Fact]
        public async Task GivenDifferentText_WhenEmbedded_ThenTheVectorsDiffer()
        {
            var embeddings = await _client.GenerateEmbeddingsAsync(new[] { "chest pain", "broken arm" }, CancellationToken.None);

            Assert.NotEqual(embeddings[0], embeddings[1]);
        }

        [Fact]
        public async Task GivenMultipleTexts_WhenEmbedded_ThenOneVectorPerTextIsReturnedInOrder()
        {
            var texts = new[] { "a", "b", "c" };

            var embeddings = await _client.GenerateEmbeddingsAsync(texts, CancellationToken.None);

            Assert.Equal(texts.Length, embeddings.Count);
        }

        [Fact]
        public async Task GivenNullTexts_WhenEmbedded_ThenArgumentNullExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GenerateEmbeddingsAsync(null, CancellationToken.None));
        }

        [Fact]
        public void GivenNonPositiveDimensions_WhenConstructed_ThenArgumentExceptionIsThrown()
        {
            Assert.ThrowsAny<ArgumentException>(() => new DeterministicEmbeddingClient(dimensions: 0));
        }
    }
}
