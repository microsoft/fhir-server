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
        /// <remarks>
        /// At most <see cref="VectorSearchConfiguration.MaxModelNameLength"/> printable ASCII characters with no leading or trailing whitespace.
        /// </remarks>
        public string ModelName { get; set; } = "text-embedding-3-small";

        /// <summary>
        /// Gets or sets the embedding model version recorded as vector provenance.
        /// </summary>
        /// <remarks>
        /// At most <see cref="VectorSearchConfiguration.MaxModelVersionLength"/> printable ASCII characters with no leading or trailing whitespace.
        /// Each distinct name and version pair identifies a separate embedding model.
        /// </remarks>
        public string ModelVersion { get; set; }

        /// <summary>
        /// Gets or sets the number of dimensions produced for each embedding.
        /// </summary>
        public int Dimensions { get; set; } = VectorSearchConfiguration.SupportedDimensions;

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Dimensions"/> is also sent to the embedding service as the
        /// request's <c>dimensions</c> parameter.
        /// </summary>
        /// <remarks>
        /// Leave <c>false</c> (the default) when the deployment natively returns <see cref="Dimensions"/> values, as
        /// <c>text-embedding-3-small</c> and <c>text-embedding-ada-002</c> do; <c>text-embedding-ada-002</c> rejects the
        /// parameter with HTTP 400. Set <c>true</c> for a model that natively returns more dimensions but can shorten its
        /// output to the requested size, such as <c>text-embedding-3-large</c>.
        /// </remarks>
        public bool SendDimensionsParameter { get; set; }
    }
}
