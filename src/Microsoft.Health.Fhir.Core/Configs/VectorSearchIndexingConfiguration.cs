// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures how vectors are generated when resources are indexed.
    /// </summary>
    public sealed class VectorSearchIndexingConfiguration
    {
        /// <summary>
        /// Gets or sets the vector indexing mode.
        /// </summary>
        public VectorSearchIndexingMode Mode { get; set; } = VectorSearchIndexingMode.Synchronous;

        /// <summary>
        /// Gets or sets the maximum number of tokens in an embedding chunk.
        /// </summary>
        public int ChunkSizeTokens { get; set; } = 800;

        /// <summary>
        /// Gets or sets the number of tokens repeated between adjacent chunks.
        /// </summary>
        public int ChunkOverlapTokens { get; set; } = 100;

        /// <summary>
        /// Gets or sets PDF text extraction settings.
        /// </summary>
        public VectorSearchPdfConfiguration Pdf { get; set; } = new VectorSearchPdfConfiguration();
    }
}
