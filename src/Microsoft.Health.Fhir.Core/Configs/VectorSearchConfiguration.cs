// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

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

            if (Indexing.EnabledSearchParameters == null || Indexing.EnabledSearchParameters.Count == 0)
            {
                throw new InvalidOperationException("At least one vector SearchParameter canonical URI is required when vector search is enabled.");
            }

            var uniqueSearchParameters = new HashSet<Uri>();
            foreach (Uri searchParameterUri in Indexing.EnabledSearchParameters)
            {
                if (searchParameterUri == null || !searchParameterUri.IsAbsoluteUri)
                {
                    throw new InvalidOperationException("Every enabled vector SearchParameter URI must be absolute.");
                }

                if (!uniqueSearchParameters.Add(searchParameterUri))
                {
                    throw new InvalidOperationException($"Vector SearchParameter URI '{searchParameterUri}' is configured more than once.");
                }
            }

            if (Indexing.ChunkSizeTokens <= 0)
            {
                throw new InvalidOperationException("Vector search chunk size must be greater than zero.");
            }

            if (Indexing.ChunkOverlapTokens < 0 || Indexing.ChunkOverlapTokens >= Indexing.ChunkSizeTokens)
            {
                throw new InvalidOperationException("Vector search chunk overlap must be non-negative and smaller than the chunk size.");
            }

            if (Query == null)
            {
                throw new InvalidOperationException("Vector search query configuration is required when vector search is enabled.");
            }

            if (Query.DefaultCount <= 0)
            {
                throw new InvalidOperationException("Vector search default result count must be greater than zero.");
            }

            if (Query.MaxCount < Query.DefaultCount)
            {
                throw new InvalidOperationException("Vector search maximum result count must be greater than or equal to the default result count.");
            }

            if (Query.CandidateCount < Query.MaxCount)
            {
                throw new InvalidOperationException("Vector search candidate count must be greater than or equal to the maximum result count.");
            }

            if (!string.Equals(Query.DistanceMetric, SupportedDistanceMetric, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Vector search distance metric must be '{SupportedDistanceMetric}'.");
            }
        }
    }
}
