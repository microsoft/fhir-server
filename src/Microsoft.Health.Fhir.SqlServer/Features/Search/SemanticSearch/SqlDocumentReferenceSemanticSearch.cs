// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// Embeds a query and ranks candidate DocumentReference vectors in SQL Server.
    /// </summary>
    public sealed class SqlDocumentReferenceSemanticSearch : IDocumentReferenceSemanticSearch
    {
        private readonly IEmbeddingClient _embeddingClient;
        private readonly IVectorStore _vectorStore;
        private readonly IEmbeddingModelRegistry _embeddingModelRegistry;
        private readonly IVectorSearchParameterResolver _searchParameterResolver;
        private readonly SqlServerFhirModel _model;
        private readonly VectorSearchConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlDocumentReferenceSemanticSearch"/> class.
        /// </summary>
        public SqlDocumentReferenceSemanticSearch(
            IEmbeddingClient embeddingClient,
            IVectorStore vectorStore,
            IEmbeddingModelRegistry embeddingModelRegistry,
            IVectorSearchParameterResolver searchParameterResolver,
            SqlServerFhirModel model,
            IOptions<VectorSearchConfiguration> configuration)
        {
            _embeddingClient = EnsureArg.IsNotNull(embeddingClient, nameof(embeddingClient));
            _vectorStore = EnsureArg.IsNotNull(vectorStore, nameof(vectorStore));
            _embeddingModelRegistry = EnsureArg.IsNotNull(embeddingModelRegistry, nameof(embeddingModelRegistry));
            _searchParameterResolver = EnsureArg.IsNotNull(searchParameterResolver, nameof(searchParameterResolver));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string query,
            IReadOnlyList<ResourceWrapper> candidates,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(query, nameof(query));
            EnsureArg.IsNotNull(candidates, nameof(candidates));

            if (candidates.Count == 0)
            {
                return System.Array.Empty<VectorSearchResult>();
            }

            IReadOnlyList<float[]> embeddings = await _embeddingClient.GenerateEmbeddingsAsync(new[] { query }, cancellationToken);
            short embeddingModelId = await _embeddingModelRegistry.GetEmbeddingModelIdAsync(cancellationToken);
            var rankedHits = new List<(string ResourceType, ResourceWrapper Owner, SearchParameterInfo SearchParameter, VectorSearchHit Hit)>();

            foreach (IGrouping<string, ResourceWrapper> candidatesByResourceType in candidates.GroupBy(candidate => candidate.ResourceTypeName, System.StringComparer.Ordinal))
            {
                string resourceType = candidatesByResourceType.Key;
                IReadOnlyList<SearchParameterInfo> searchParameters = _searchParameterResolver.GetSearchParameters(resourceType);
                IReadOnlyList<long> candidateIds = candidatesByResourceType.Select(candidate => candidate.ResourceSurrogateId).ToList();
                Dictionary<long, ResourceWrapper> candidatesBySurrogateId = candidatesByResourceType.ToDictionary(candidate => candidate.ResourceSurrogateId);

                foreach (SearchParameterInfo searchParameter in searchParameters)
                {
                    IReadOnlyList<VectorSearchHit> targetHits = await _vectorStore.SearchAsync(
                        _model.GetResourceTypeId(resourceType),
                        _model.GetSearchParamId(searchParameter.Url),
                        embeddingModelId,
                        _configuration.Query.DistanceMetric,
                        embeddings[0],
                        candidateIds,
                        count,
                        _configuration.Query.EvidenceCount,
                        cancellationToken);

                    foreach (VectorSearchHit hit in targetHits)
                    {
                        ResourceWrapper owner = candidatesBySurrogateId[hit.ResourceSurrogateId];
                        rankedHits.Add((resourceType, owner, searchParameter, hit));
                    }
                }
            }

            return rankedHits
                .GroupBy(result => (result.ResourceType, result.Hit.ResourceSurrogateId))
                .Select(group =>
                {
                    var orderedHits = group
                        .OrderByDescending(result => result.Hit.Score)
                        .ThenBy(result => result.Hit.ChunkOrdinal)
                        .ThenBy(result => result.SearchParameter.Url.AbsoluteUri, System.StringComparer.Ordinal)
                        .Take(_configuration.Query.EvidenceCount)
                        .ToList();
                    IReadOnlyList<SemanticSearchEvidence> evidenceItems = orderedHits
                        .Select(result => CreateEvidence(result.Owner, result.SearchParameter, result.Hit))
                        .ToList();

                    return new VectorSearchResult(
                        group.Key.ResourceType,
                        group.Key.ResourceSurrogateId,
                        orderedHits[0].Hit.Score,
                        evidenceItems);
                })
                .OrderByDescending(result => result.Score)
                .Take(count)
                .ToList();
        }

        private SemanticSearchEvidence CreateEvidence(
            ResourceWrapper owner,
            SearchParameterInfo searchParameter,
            VectorSearchHit hit)
        {
            string sourceResourceType = hit.SourceResourceTypeId.HasValue
                ? _model.GetResourceTypeName(hit.SourceResourceTypeId.Value)
                : owner.ResourceTypeName;
            var sourceKey = new ResourceKey(
                sourceResourceType,
                hit.SourceResourceId ?? owner.ResourceId,
                hit.SourceResourceVersion ?? owner.Version);

            return new SemanticSearchEvidence(
                hit.ChunkText,
                hit.ChunkOrdinal,
                (decimal)hit.Score,
                searchParameter.Url,
                sourceKey.ToString(),
                hit.SourcePath ?? searchParameter.Expression);
        }
    }
}
