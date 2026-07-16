// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// One ranked passage returned by a semantic search: which resource and passage matched, and how relevant it is.
    /// </summary>
    public sealed class VectorSearchResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchResult"/> class.
        /// </summary>
        /// <param name="resourceSurrogateId">The surrogate id of the resource that matched.</param>
        /// <param name="chunkOrdinal">The zero-based ordinal of the passage that matched within its source document.</param>
        /// <param name="score">The relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.</param>
        public VectorSearchResult(long resourceSurrogateId, int chunkOrdinal, float score)
        {
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));

            ResourceSurrogateId = resourceSurrogateId;
            ChunkOrdinal = chunkOrdinal;
            Score = score;
        }

        /// <summary>
        /// Gets the surrogate id of the resource that matched.
        /// </summary>
        public long ResourceSurrogateId { get; }

        /// <summary>
        /// Gets the zero-based ordinal of the passage that matched within its source document.
        /// </summary>
        public int ChunkOrdinal { get; }

        /// <summary>
        /// Gets the relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.
        /// </summary>
        public float Score { get; }
    }
}
