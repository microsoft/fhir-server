// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Contains vector-specific metadata carried by a FHIR SearchParameter extension.
    /// </summary>
    public sealed class VectorSearchParameterConfig
    {
        /// <summary>
        /// Gets the canonical URL of the vector SearchParameter configuration extension.
        /// </summary>
        public const string ExtensionUrl = "http://microsoft.com/fhir/StructureDefinition/vector-search-config";

        /// <summary>
        /// Gets the nested extension URL for the extraction policy.
        /// </summary>
        public const string ExtractionPolicyExtensionUrl = "extractionPolicy";

        /// <summary>
        /// Gets the nested extension URL for the source strategy.
        /// </summary>
        public const string SourceStrategyExtensionUrl = "sourceStrategy";

        /// <summary>
        /// Gets the nested extension URL for the maximum input token count.
        /// </summary>
        public const string MaxInputTokensExtensionUrl = "maxInputTokens";

        /// <summary>
        /// Gets the nested extension URL for the minimum normalized relevance score.
        /// </summary>
        public const string MinimumScoreExtensionUrl = "minimumScore";

        /// <summary>
        /// Gets the nested extension URL for the chunk size.
        /// </summary>
        public const string ChunkSizeTokensExtensionUrl = "chunkSizeTokens";

        /// <summary>
        /// Gets the nested extension URL for the chunk overlap.
        /// </summary>
        public const string ChunkOverlapTokensExtensionUrl = "chunkOverlapTokens";

        /// <summary>
        /// Gets the nested extension URL for the vector distance metric.
        /// </summary>
        public const string DistanceMetricExtensionUrl = "distanceMetric";

        /// <summary>
        /// Gets or sets the policy used to turn expression values into source passages.
        /// </summary>
        public VectorTextExtractionPolicy ExtractionPolicy { get; set; } = VectorTextExtractionPolicy.Concatenate;

        /// <summary>
        /// Gets or sets the strategy used to resolve expression values to source text.
        /// </summary>
        public VectorTextSourceStrategy SourceStrategy { get; set; } = VectorTextSourceStrategy.DirectText;

        /// <summary>
        /// Gets or sets the maximum number of source tokens accepted from this SearchParameter.
        /// </summary>
        public int MaxInputTokens { get; set; } = 8000;

        /// <summary>
        /// Gets or sets the minimum normalized relevance score required for a chunk to match.
        /// </summary>
        public decimal MinimumScore { get; set; }

        /// <summary>
        /// Gets or sets the optional chunk size. A null value uses the server default.
        /// </summary>
        public int? ChunkSizeTokens { get; set; }

        /// <summary>
        /// Gets or sets the optional chunk overlap. A null value uses the server default.
        /// </summary>
        public int? ChunkOverlapTokens { get; set; }

        /// <summary>
        /// Gets or sets the optional vector distance metric. A null value uses the server default.
        /// </summary>
        public string DistanceMetric { get; set; }
    }
}
