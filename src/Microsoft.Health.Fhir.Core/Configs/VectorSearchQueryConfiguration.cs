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
        /// The largest supported <see cref="ApproximateCandidateMultiplier"/>.
        /// </summary>
        public const int MaximumApproximateCandidateMultiplier = 100;

        /// <summary>
        /// Gets or sets the vector distance metric.
        /// </summary>
        public string DistanceMetric { get; set; } = VectorSearchConfiguration.SupportedDistanceMetric;

        /// <summary>
        /// Gets or sets whether relevance-ranked queries may use an approximate vector index.
        /// </summary>
        /// <remarks>
        /// Approximate retrieval is opt-in because it can return fewer or slightly different results than exact ranking.
        /// Queries that count results or request an explicit sort always rank exactly.
        /// </remarks>
        public VectorSearchApproximateMode ApproximateSearch { get; set; } = VectorSearchApproximateMode.Disabled;

        /// <summary>
        /// Gets or sets how many nearest chunks an approximate query examines for each requested result.
        /// </summary>
        /// <remarks>
        /// Several chunks can belong to one resource, so a page needs more candidate chunks than resources.
        /// A larger value fills pages more reliably but examines more of the index.
        /// </remarks>
        public int ApproximateCandidateMultiplier { get; set; } = 10;
    }
}
