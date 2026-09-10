// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TextChunkerTests
    {
        private readonly TextChunker _chunker = new TextChunker(
            Options.Create(
                new VectorSearchConfiguration
                {
                    Embedding = new VectorSearchEmbeddingConfiguration
                    {
                        ModelName = "text-embedding-3-small",
                    },
                }));

        [Fact]
        public void GivenNullText_WhenChunked_ThenArgumentNullExceptionIsThrown()
        {
            Assert.Throws<ArgumentNullException>(() => _chunker.Chunk(null, maxInputTokens: 100, chunkSizeTokens: 10, chunkOverlapTokens: 2));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 0)]
        public void GivenNonPositiveChunkSize_WhenChunked_ThenArgumentExceptionIsThrown(int chunkSize, int chunkOverlap)
        {
            Assert.ThrowsAny<ArgumentException>(() => _chunker.Chunk("some text", maxInputTokens: 100, chunkSize, chunkOverlap));
        }

        [Theory]
        [InlineData(4, 4)]
        [InlineData(4, 5)]
        [InlineData(4, -1)]
        public void GivenInvalidOverlap_WhenChunked_ThenArgumentExceptionIsThrown(int chunkSize, int chunkOverlap)
        {
            Assert.ThrowsAny<ArgumentException>(() => _chunker.Chunk("some text", maxInputTokens: 100, chunkSize, chunkOverlap));
        }

        [Fact]
        public void GivenEmptyText_WhenChunked_ThenNoChunksAreReturned()
        {
            Assert.Empty(_chunker.Chunk(string.Empty, maxInputTokens: 100, chunkSizeTokens: 10, chunkOverlapTokens: 2));
        }

        [Fact]
        public void GivenTextShorterThanChunkSize_WhenChunked_ThenASingleChunkEqualToTheTextIsReturned()
        {
            Assert.Equal(new[] { "short" }, _chunker.Chunk("short", maxInputTokens: 100, chunkSizeTokens: 10, chunkOverlapTokens: 2));
        }

        [Fact]
        public void GivenTextEqualToChunkSize_WhenChunked_ThenASingleChunkIsReturned()
        {
            Assert.Equal(new[] { "abcd" }, _chunker.Chunk("abcd", maxInputTokens: 100, chunkSizeTokens: 4, chunkOverlapTokens: 1));
        }

        [Fact]
        public void GivenOverlap_WhenChunked_ThenAdjacentChunksShareTheOverlap()
        {
            Assert.Equal(new[] { " one two three", " three four five" }, _chunker.Chunk(" one two three four five", maxInputTokens: 100, chunkSizeTokens: 3, chunkOverlapTokens: 1));
        }

        [Fact]
        public void GivenNoOverlap_WhenChunked_ThenChunksArePartitionedWithoutSharing()
        {
            Assert.Equal(new[] { " one two three", " four five six" }, _chunker.Chunk(" one two three four five six", maxInputTokens: 100, chunkSizeTokens: 3, chunkOverlapTokens: 0));
        }

        [Fact]
        public void GivenAnyText_WhenChunked_ThenTheLastChunkEndsAtTheEndOfTheText()
        {
            const string text = " one two three four five six seven";

            var chunks = _chunker.Chunk(text, maxInputTokens: 100, chunkSizeTokens: 3, chunkOverlapTokens: 1);

            Assert.EndsWith(chunks[chunks.Count - 1], text);
        }

        [Fact]
        public void GivenInputExceedingMaximumTokens_WhenChunked_ThenOnlyTheConfiguredTokenPrefixIsReturned()
        {
            const string text = " one two three four five";

            IReadOnlyList<string> chunks = _chunker.Chunk(text, maxInputTokens: 4, chunkSizeTokens: 2, chunkOverlapTokens: 0);

            Assert.Equal(" one two three four", string.Concat(chunks));
            Assert.Equal(4, chunks.Sum(_chunker.CountTokens));
        }

        [Fact]
        public void GivenMultibyteText_WhenChunked_ThenChunksRespectTokenLimitsAndUnicodeBoundaries()
        {
            const string text = "🙂 café 東京 🙂 café 東京";

            IReadOnlyList<string> chunks = _chunker.Chunk(text, maxInputTokens: 20, chunkSizeTokens: 4, chunkOverlapTokens: 1);

            Assert.All(chunks, chunk =>
            {
                Assert.InRange(_chunker.CountTokens(chunk), 1, 4);
                Assert.DoesNotContain('\uFFFD', chunk);
                Assert.False(char.IsLowSurrogate(chunk[0]));
                Assert.False(char.IsHighSurrogate(chunk[chunk.Length - 1]));
            });
        }
    }
}
