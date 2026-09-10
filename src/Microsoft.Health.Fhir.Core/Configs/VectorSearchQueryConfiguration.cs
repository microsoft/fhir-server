// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures vector search result limits.
    /// </summary>
    public sealed class VectorSearchQueryConfiguration
    {
        /// <summary>
        /// Gets or sets the default number of semantic search results.
        /// </summary>
        public int DefaultCount { get; set; } = 10;

        /// <summary>
        /// Gets or sets the maximum number of semantic search results.
        /// </summary>
        public int MaxCount { get; set; } = 50;

        /// <summary>
        /// Gets or sets the number of structured-search candidates considered for semantic ranking.
        /// </summary>
        public int CandidateCount { get; set; } = 100;

        /// <summary>
        /// Gets or sets the vector distance metric.
        /// </summary>
        public string DistanceMetric { get; set; } = VectorSearchConfiguration.SupportedDistanceMetric;
    }
}
