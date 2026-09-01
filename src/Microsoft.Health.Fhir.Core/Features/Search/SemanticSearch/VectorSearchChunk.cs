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
        /// <param name="sourceResourceType">The source resource type.</param>
        /// <param name="sourceResourceId">The source resource id.</param>
        /// <param name="sourceResourceVersion">The source resource version.</param>
        /// <param name="sourcePath">The source element path.</param>
        public VectorSearchChunk(
            int chunkOrdinal,
            string chunkText,
            IReadOnlyList<byte> sourceTextHash,
            IReadOnlyList<float> embedding,
            string sourceResourceType = null,
            string sourceResourceId = null,
            string sourceResourceVersion = null,
            string sourcePath = null)
        {
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));
            EnsureArg.IsNotNull(chunkText, nameof(chunkText));
            EnsureArg.IsNotNull(sourceTextHash, nameof(sourceTextHash));
            EnsureArg.IsNotNull(embedding, nameof(embedding));

            ChunkOrdinal = chunkOrdinal;
            ChunkText = chunkText;
            SourceTextHash = sourceTextHash;
            Embedding = embedding;
            SourceResourceType = sourceResourceType;
            SourceResourceId = sourceResourceId;
            SourceResourceVersion = sourceResourceVersion;
            SourcePath = sourcePath;
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
        /// Gets the hash of the exact passage text, used to skip re-embedding unchanged text.
        /// </summary>
        public IReadOnlyList<byte> SourceTextHash { get; }

        /// <summary>
        /// Gets the embedding vector for this passage.
        /// </summary>
        public IReadOnlyList<float> Embedding { get; }

        /// <summary>
        /// Gets the type of the FHIR resource containing the source text.
        /// </summary>
        public string SourceResourceType { get; }

        /// <summary>
        /// Gets the id of the FHIR resource containing the source text.
        /// </summary>
        public string SourceResourceId { get; }

        /// <summary>
        /// Gets the version of the FHIR resource containing the source text.
        /// </summary>
        public string SourceResourceVersion { get; }

        /// <summary>
        /// Gets the path of the source text within the FHIR resource.
        /// </summary>
        public string SourcePath { get; }
    }
}
