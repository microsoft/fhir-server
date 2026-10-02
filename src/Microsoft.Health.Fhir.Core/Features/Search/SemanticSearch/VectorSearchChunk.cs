// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// One passage's embedding vector, identified by its ordinal within the source document.
    /// </summary>
    public sealed class VectorSearchChunk
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchChunk"/> class.
        /// </summary>
        /// <param name="chunkOrdinal">The zero-based ordinal of this passage within the source document.</param>
        /// <param name="chunkText">The exact passage text represented by the embedding.</param>
        /// <param name="sourceTextHash">The hash of the exact passage text.</param>
        /// <param name="embedding">The embedding vector for this passage.</param>
        public VectorSearchChunk(
            int chunkOrdinal,
            string chunkText,
            IReadOnlyList<byte> sourceTextHash,
            IReadOnlyList<float> embedding)
        {
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));
            EnsureArg.IsNotNull(chunkText, nameof(chunkText));
            EnsureArg.IsNotNull(sourceTextHash, nameof(sourceTextHash));
            EnsureArg.IsNotNull(embedding, nameof(embedding));

            ChunkOrdinal = chunkOrdinal;
            ChunkText = chunkText;
            SourceTextHash = sourceTextHash;
            Embedding = embedding;
        }

        /// <summary>
        /// Gets the zero-based ordinal of this passage within the source document.
        /// </summary>
        public int ChunkOrdinal { get; }

        /// <summary>
        /// Gets the exact passage text represented by the embedding.
        /// </summary>
        public string ChunkText { get; }

        /// <summary>
        /// Gets the hash of the exact passage text.
        /// </summary>
        public IReadOnlyList<byte> SourceTextHash { get; }

        /// <summary>
        /// Gets the embedding vector for this passage.
        /// </summary>
        public IReadOnlyList<float> Embedding { get; }
    }
}
