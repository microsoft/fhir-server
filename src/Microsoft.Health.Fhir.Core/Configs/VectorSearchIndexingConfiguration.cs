// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

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
        /// Gets or sets the canonical URIs of the FHIR SearchParameters enabled for vector indexing.
        /// </summary>
        public IList<Uri> EnabledSearchParameters { get; } = new List<Uri>();

        /// <summary>
        /// Gets or sets the maximum number of tokens in an embedding chunk.
        /// </summary>
        public int ChunkSizeTokens { get; set; } = 800;

        /// <summary>
        /// Gets or sets the number of tokens repeated between adjacent chunks.
        /// </summary>
        public int ChunkOverlapTokens { get; set; } = 100;
    }
}
