// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Represents one ranked vector-store hit before FHIR source provenance is attached.
    /// </summary>
    public sealed class VectorSearchHit
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchHit"/> class.
        /// </summary>
        /// <param name="resourceSurrogateId">The surrogate id of the resource that matched.</param>
        /// <param name="chunkOrdinal">The zero-based ordinal of the matched passage.</param>
        /// <param name="chunkText">The exact text represented by the matched embedding.</param>
        /// <param name="score">The normalized relevance score, where higher is more relevant.</param>
        /// <param name="sourceResourceTypeId">The storage resource type id containing the source text.</param>
        /// <param name="sourceResourceId">The id of the resource containing the source text.</param>
        /// <param name="sourceResourceVersion">The version of the resource containing the source text.</param>
        /// <param name="sourcePath">The path of the source text within its resource.</param>
        public VectorSearchHit(
            long resourceSurrogateId,
            int chunkOrdinal,
            string chunkText,
            float score,
            short? sourceResourceTypeId = null,
            string sourceResourceId = null,
            string sourceResourceVersion = null,
            string sourcePath = null)
        {
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));
            EnsureArg.IsNotNullOrWhiteSpace(chunkText, nameof(chunkText));

            ResourceSurrogateId = resourceSurrogateId;
            ChunkOrdinal = chunkOrdinal;
            ChunkText = chunkText;
            Score = score;
            SourceResourceTypeId = sourceResourceTypeId;
            SourceResourceId = sourceResourceId;
            SourceResourceVersion = sourceResourceVersion;
            SourcePath = sourcePath;
        }

        /// <summary>
        /// Gets the surrogate id of the resource that matched.
        /// </summary>
        public long ResourceSurrogateId { get; }

        /// <summary>
        /// Gets the zero-based ordinal of the matched passage.
        /// </summary>
        public int ChunkOrdinal { get; }

        /// <summary>
        /// Gets the exact text represented by the matched embedding.
        /// </summary>
        public string ChunkText { get; }

        /// <summary>
        /// Gets the normalized relevance score, where higher is more relevant.
        /// </summary>
        public float Score { get; }

        /// <summary>
        /// Gets the storage resource type id containing the source text.
        /// </summary>
        public short? SourceResourceTypeId { get; }

        /// <summary>
        /// Gets the id of the resource containing the source text.
        /// </summary>
        public string SourceResourceId { get; }

        /// <summary>
        /// Gets the version of the resource containing the source text.
        /// </summary>
        public string SourceResourceVersion { get; }

        /// <summary>
        /// Gets the path of the source text within its resource.
        /// </summary>
        public string SourcePath { get; }
    }
}
