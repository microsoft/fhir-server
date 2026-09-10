// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures the embedding service used to generate vectors.
    /// </summary>
    public sealed class VectorSearchEmbeddingConfiguration
    {
        /// <summary>
        /// Gets or sets the embedding service endpoint.
        /// </summary>
        public Uri Endpoint { get; set; }

        /// <summary>
        /// Gets or sets the embedding deployment name.
        /// </summary>
        public string DeploymentName { get; set; }

        /// <summary>
        /// Gets or sets the embedding model name recorded as vector provenance.
        /// </summary>
        public string ModelName { get; set; } = "text-embedding-3-small";

        /// <summary>
        /// Gets or sets the embedding model version recorded as vector provenance.
        /// </summary>
        public string ModelVersion { get; set; }

        /// <summary>
        /// Gets or sets the number of dimensions produced for each embedding.
        /// </summary>
        public int Dimensions { get; set; } = VectorSearchConfiguration.SupportedDimensions;
    }
}
