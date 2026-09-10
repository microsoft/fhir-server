// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// One ranked FHIR semantic-search result.
    /// </summary>
    public sealed class VectorSearchResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchResult"/> class.
        /// </summary>
        /// <param name="resourceTypeName">The FHIR resource type that matched.</param>
        /// <param name="resourceSurrogateId">The surrogate id of the resource that matched.</param>
        /// <param name="score">The relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.</param>
        public VectorSearchResult(string resourceTypeName, long resourceSurrogateId, float score)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceTypeName, nameof(resourceTypeName));

            ResourceTypeName = resourceTypeName;
            ResourceSurrogateId = resourceSurrogateId;
            Score = score;
        }

        /// <summary>
        /// Gets the FHIR resource type that matched.
        /// </summary>
        public string ResourceTypeName { get; }

        /// <summary>
        /// Gets the surrogate id of the resource that matched.
        /// </summary>
        public long ResourceSurrogateId { get; }

        /// <summary>
        /// Gets the relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.
        /// </summary>
        public float Score { get; }
    }
}
