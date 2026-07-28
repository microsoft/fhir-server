// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Stores per-passage embedding vectors so they can later be ranked by similarity.
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Stores the embedding vectors for one resource's passages.
        /// </summary>
        /// <param name="resourceTypeId">The id of the FHIR resource type the vectors belong to.</param>
        /// <param name="resourceSurrogateId">The surrogate id of the resource the vectors belong to.</param>
        /// <param name="searchParamId">The id of the semantic search parameter that produced the vectors.</param>
        /// <param name="embeddingModelId">The id of the embedding model that produced the vectors.</param>
        /// <param name="chunks">The per-passage vectors to store.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StoreAsync(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            short embeddingModelId,
            IReadOnlyList<VectorSearchChunk> chunks,
            CancellationToken cancellationToken);

        /// <summary>
        /// Ranks the passages of a pre-filtered candidate set by how close they are to the query vector.
        /// The candidate set is the result of the structured filter (patient, encounter, date, and so on),
        /// so ranking always runs over records the caller is already allowed to see.
        /// </summary>
        /// <param name="resourceTypeId">The id of the FHIR resource type to rank.</param>
        /// <param name="searchParamId">The id of the semantic search parameter that produced the vectors.</param>
        /// <param name="embeddingModelId">The id of the embedding model whose vectors to rank, so the query and stored vectors share one space.</param>
        /// <param name="distanceMetric">The vector distance metric.</param>
        /// <param name="queryEmbedding">The embedding of the search string.</param>
        /// <param name="candidateResourceSurrogateIds">The surrogate ids that passed the structured filter.</param>
        /// <param name="maxResults">The maximum number of passages to return, ordered by relevance.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The closest passages, ordered from most to least relevant.</returns>
        Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            short resourceTypeId,
            short searchParamId,
            short embeddingModelId,
            string distanceMetric,
            IReadOnlyList<float> queryEmbedding,
            IReadOnlyList<long> candidateResourceSurrogateIds,
            int maxResults,
            CancellationToken cancellationToken);
    }
}
