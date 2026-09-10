// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly TextChunker _chunker = new TextChunker();

        [Fact]
        public void GivenNullText_WhenChunked_ThenArgumentNullExceptionIsThrown()
        {
            Assert.Throws<ArgumentNullException>(() => _chunker.Chunk(null, chunkSize: 10, chunkOverlap: 2));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 0)]
        public void GivenNonPositiveChunkSize_WhenChunked_ThenArgumentExceptionIsThrown(int chunkSize, int chunkOverlap)
        {
            Assert.ThrowsAny<ArgumentException>(() => _chunker.Chunk("some text", chunkSize, chunkOverlap));
        }

        [Theory]
        [InlineData(4, 4)]
        [InlineData(4, 5)]
        [InlineData(4, -1)]
        public void GivenInvalidOverlap_WhenChunked_ThenArgumentExceptionIsThrown(int chunkSize, int chunkOverlap)
        {
            Assert.ThrowsAny<ArgumentException>(() => _chunker.Chunk("some text", chunkSize, chunkOverlap));
        }

        [Fact]
        public void GivenEmptyText_WhenChunked_ThenNoChunksAreReturned()
        {
            Assert.Empty(_chunker.Chunk(string.Empty, chunkSize: 10, chunkOverlap: 2));
        }

        [Fact]
        public void GivenTextShorterThanChunkSize_WhenChunked_ThenASingleChunkEqualToTheTextIsReturned()
        {
            Assert.Equal(new[] { "short" }, _chunker.Chunk("short", chunkSize: 10, chunkOverlap: 2));
        }

        [Fact]
        public void GivenTextEqualToChunkSize_WhenChunked_ThenASingleChunkIsReturned()
        {
            Assert.Equal(new[] { "abcd" }, _chunker.Chunk("abcd", chunkSize: 4, chunkOverlap: 1));
        }

        [Fact]
        public void GivenOverlap_WhenChunked_ThenAdjacentChunksShareTheOverlap()
        {
            Assert.Equal(new[] { "abcd", "defg", "ghij" }, _chunker.Chunk("abcdefghij", chunkSize: 4, chunkOverlap: 1));
        }

        [Fact]
        public void GivenNoOverlap_WhenChunked_ThenChunksArePartitionedWithoutSharing()
        {
            Assert.Equal(new[] { "abcde", "fghij" }, _chunker.Chunk("abcdefghij", chunkSize: 5, chunkOverlap: 0));
        }

        [Fact]
        public void GivenAnyText_WhenChunked_ThenTheLastChunkEndsAtTheEndOfTheText()
        {
            const string text = "abcdefghijklmno";

            var chunks = _chunker.Chunk(text, chunkSize: 6, chunkOverlap: 2);

            Assert.EndsWith(chunks[chunks.Count - 1], text);
        }
    }
}
