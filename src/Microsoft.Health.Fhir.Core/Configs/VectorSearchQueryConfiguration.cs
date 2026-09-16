// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures vector search query behavior.
    /// </summary>
    public sealed class VectorSearchQueryConfiguration
    {
        /// <summary>
        /// Gets or sets the vector distance metric.
        /// </summary>
        public string DistanceMetric { get; set; } = VectorSearchConfiguration.SupportedDistanceMetric;
    }
}
