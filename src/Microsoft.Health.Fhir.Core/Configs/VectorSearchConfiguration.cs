// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures vector generation, persistence, and query limits for semantic search.
    /// </summary>
    public sealed class VectorSearchConfiguration
    {
        /// <summary>
        /// The vector width supported by the current SQL schema.
        /// </summary>
        public const int SupportedDimensions = 1536;

        /// <summary>
        /// The maximum number of tokens supported by one embedding input.
        /// </summary>
        public const int MaxEmbeddingInputTokens = 8192;

        /// <summary>
        /// The minimum chunk size that can contain any single Unicode scalar with the supported byte-level tokenizer.
        /// </summary>
        public const int MinimumChunkSizeTokens = 4;

        /// <summary>
        /// The distance metric supported by the current semantic score calculation.
        /// </summary>
        public const string SupportedDistanceMetric = "cosine";

        /// <summary>
        /// Gets or sets a value indicating whether vector search is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the embedding service configuration.
        /// </summary>
        public VectorSearchEmbeddingConfiguration Embedding { get; set; } = new VectorSearchEmbeddingConfiguration();

        /// <summary>
        /// Gets or sets the vector indexing configuration.
        /// </summary>
        public VectorSearchIndexingConfiguration Indexing { get; set; } = new VectorSearchIndexingConfiguration();

        /// <summary>
        /// Gets or sets the vector query configuration.
        /// </summary>
        public VectorSearchQueryConfiguration Query { get; set; } = new VectorSearchQueryConfiguration();

        /// <summary>
        /// Validates settings that are required when vector search is enabled.
        /// </summary>
        /// <exception cref="InvalidOperationException">The enabled configuration is incomplete or incompatible.</exception>
        public void Validate()
        {
            if (!Enabled)
            {
                return;
            }

            if (Embedding == null)
            {
                throw new InvalidOperationException("Vector search embedding configuration is required when vector search is enabled.");
            }

            if (Embedding.Endpoint == null || !Embedding.Endpoint.IsAbsoluteUri || !string.Equals(Embedding.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Vector search embedding endpoint must be an absolute HTTPS URI when vector search is enabled.");
            }

            if (string.IsNullOrWhiteSpace(Embedding.DeploymentName))
            {
                throw new InvalidOperationException("Vector search embedding deployment name is required when vector search is enabled.");
            }

            if (string.IsNullOrWhiteSpace(Embedding.ModelName))
            {
                throw new InvalidOperationException("Vector search embedding model name is required when vector search is enabled.");
            }

            if (string.IsNullOrWhiteSpace(Embedding.ModelVersion))
            {
                throw new InvalidOperationException("Vector search embedding model version is required when vector search is enabled.");
            }

            if (Embedding.Dimensions != SupportedDimensions)
            {
                throw new InvalidOperationException($"Vector search embedding dimensions must be {SupportedDimensions} to match the current SQL vector schema.");
            }

            if (Indexing == null)
            {
                throw new InvalidOperationException("Vector search indexing configuration is required when vector search is enabled.");
            }

            if (Indexing.Mode != VectorSearchIndexingMode.Synchronous)
            {
                throw new InvalidOperationException($"Vector search indexing mode '{Indexing.Mode}' is not supported.");
            }

            if (!TryResolveChunkSettings(vectorConfig: null, out _, out _, out string chunkSettingsError))
            {
                throw new InvalidOperationException(chunkSettingsError);
            }

            if (Query == null)
            {
                throw new InvalidOperationException("Vector search query configuration is required when vector search is enabled.");
            }

            if (!string.Equals(Query.DistanceMetric, SupportedDistanceMetric, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Vector search distance metric must be '{SupportedDistanceMetric}'.");
            }
        }

        /// <summary>
        /// Resolves optional SearchParameter chunk overrides against the server defaults and validates the effective pair.
        /// </summary>
        /// <param name="vectorConfig">The SearchParameter vector configuration, or <see langword="null"/> for server defaults.</param>
        /// <param name="chunkSizeTokens">The effective chunk size.</param>
        /// <param name="chunkOverlapTokens">The effective chunk overlap.</param>
        /// <param name="errorMessage">The validation error when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when the effective chunk settings are valid; otherwise <see langword="false"/>.</returns>
        public bool TryResolveChunkSettings(
            VectorSearchParameterConfig vectorConfig,
            out int chunkSizeTokens,
            out int chunkOverlapTokens,
            out string errorMessage)
        {
            if (Indexing == null)
            {
                chunkSizeTokens = 0;
                chunkOverlapTokens = 0;
                errorMessage = "Vector search indexing configuration is required.";
                return false;
            }

            chunkSizeTokens = vectorConfig?.ChunkSizeTokens ?? Indexing.ChunkSizeTokens;
            chunkOverlapTokens = vectorConfig?.ChunkOverlapTokens ?? Indexing.ChunkOverlapTokens;

            if (chunkSizeTokens < MinimumChunkSizeTokens || chunkSizeTokens > MaxEmbeddingInputTokens)
            {
                errorMessage = $"Vector search chunk size must be between {MinimumChunkSizeTokens} and {MaxEmbeddingInputTokens} tokens.";
                return false;
            }

            if (chunkOverlapTokens < 0 || chunkOverlapTokens >= chunkSizeTokens)
            {
                errorMessage = "Vector search chunk overlap must be non-negative and smaller than the chunk size.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
