// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// One ranked FHIR semantic-search result and the evidence passage that supports it.
    /// </summary>
    public sealed class VectorSearchResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchResult"/> class.
        /// </summary>
        /// <param name="resourceTypeName">The FHIR resource type that matched.</param>
        /// <param name="resourceSurrogateId">The surrogate id of the resource that matched.</param>
        /// <param name="score">The relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.</param>
        /// <param name="evidence">The exact passage and FHIR source provenance supporting the result.</param>
        public VectorSearchResult(string resourceTypeName, long resourceSurrogateId, float score, SemanticSearchEvidence evidence)
            : this(resourceTypeName, resourceSurrogateId, score, new[] { evidence })
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchResult"/> class.
        /// </summary>
        /// <param name="resourceTypeName">The FHIR resource type that matched.</param>
        /// <param name="resourceSurrogateId">The surrogate id of the resource that matched.</param>
        /// <param name="score">The relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.</param>
        /// <param name="evidenceItems">The supporting passages ordered by relevance within this resource.</param>
        public VectorSearchResult(string resourceTypeName, long resourceSurrogateId, float score, IReadOnlyList<SemanticSearchEvidence> evidenceItems)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceTypeName, nameof(resourceTypeName));
            EnsureArg.IsNotNull(evidenceItems, nameof(evidenceItems));
            EnsureArg.HasItems(evidenceItems, nameof(evidenceItems));

            ResourceTypeName = resourceTypeName;
            ResourceSurrogateId = resourceSurrogateId;
            Score = score;
            EvidenceItems = evidenceItems;
            Evidence = evidenceItems[0];
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
        /// Gets the exact passage and FHIR source provenance supporting the result.
        /// </summary>
        public SemanticSearchEvidence Evidence { get; }

        /// <summary>
        /// Gets the supporting passages ordered by relevance within this resource.
        /// </summary>
        public IReadOnlyList<SemanticSearchEvidence> EvidenceItems { get; }

        /// <summary>
        /// Gets the relevance score from 0 (unrelated) to 1 (identical), where higher is more relevant.
        /// </summary>
        public float Score { get; }
    }
}
