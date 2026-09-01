// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Generates embeddings from values extracted by enabled FHIR SearchParameters.
    /// </summary>
    public sealed class VectorSearchIndexer : IVectorSearchIndexer
    {
        private const string ConcatenatedValueSeparator = "\n";

        private readonly IVectorSearchParameterResolver _searchParameterResolver;
        private readonly ITextChunker _textChunker;
        private readonly IEmbeddingClient _embeddingClient;
        private readonly IEmbeddingModelRegistry _embeddingModelRegistry;
        private readonly IVectorTextSourceResolver _textSourceResolver;
        private readonly VectorSearchIndexingConfiguration _configuration;
        private readonly ILogger<VectorSearchIndexer> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchIndexer"/> class.
        /// </summary>
        public VectorSearchIndexer(
            IVectorSearchParameterResolver searchParameterResolver,
            ITextChunker textChunker,
            IEmbeddingClient embeddingClient,
            IEmbeddingModelRegistry embeddingModelRegistry,
            IVectorTextSourceResolver textSourceResolver,
            IOptions<VectorSearchConfiguration> configuration,
            ILogger<VectorSearchIndexer> logger)
        {
            _searchParameterResolver = EnsureArg.IsNotNull(searchParameterResolver, nameof(searchParameterResolver));
            _textChunker = EnsureArg.IsNotNull(textChunker, nameof(textChunker));
            _embeddingClient = EnsureArg.IsNotNull(embeddingClient, nameof(embeddingClient));
            _embeddingModelRegistry = EnsureArg.IsNotNull(embeddingModelRegistry, nameof(embeddingModelRegistry));
            _textSourceResolver = EnsureArg.IsNotNull(textSourceResolver, nameof(textSourceResolver));
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value.Indexing;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        public async Task IndexAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            var entriesByResource = resources.ToDictionary(resource => resource, _ => new List<VectorSearchIndexEntry>());
            var pendingIndices = new List<PendingVectorIndex>();
            var passages = new List<VectorTextSource>();

            foreach (ResourceWrapper resource in resources)
            {
                resource.UpdateVectorSearchIndices(Array.Empty<VectorSearchIndexEntry>());

                if (resource.IsDeleted || resource.IsHistory)
                {
                    continue;
                }

                foreach (SearchParameterInfo searchParameter in _searchParameterResolver.GetIndexingSearchParameters(resource.ResourceTypeName))
                {
                    IReadOnlyList<string> extractedValues = GetExtractedValues(resource, searchParameter);
                    IReadOnlyList<VectorTextSource> resolvedSources = await _textSourceResolver.ResolveAsync(
                        resource,
                        searchParameter,
                        extractedValues,
                        resources,
                        cancellationToken);
                    IReadOnlyList<VectorTextSource> sourceTexts = ApplyExtractionPolicy(searchParameter.VectorConfig.ExtractionPolicy, resolvedSources);
                    var chunks = new List<VectorTextSource>();
                    int configuredChunkSize = searchParameter.VectorConfig.ChunkSizeTokens ?? _configuration.ChunkSizeTokens;
                    int configuredChunkOverlap = searchParameter.VectorConfig.ChunkOverlapTokens ?? _configuration.ChunkOverlapTokens;
                    int chunkSize = Math.Min(configuredChunkSize, searchParameter.VectorConfig.MaxInputTokens);
                    int chunkOverlap = Math.Min(configuredChunkOverlap, chunkSize - 1);

                    foreach (VectorTextSource sourceText in sourceTexts)
                    {
                        chunks.AddRange(_textChunker
                            .Chunk(sourceText.Text, chunkSize, chunkOverlap)
                            .Select(text => new VectorTextSource(text, sourceText.ResourceType, sourceText.ResourceId, sourceText.ResourceVersion, sourceText.Path)));
                    }

                    if (chunks.Count == 0)
                    {
                        continue;
                    }

                    if (chunks.Count > short.MaxValue)
                    {
                        throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' generated more than {short.MaxValue} passages for one resource.");
                    }

                    pendingIndices.Add(new PendingVectorIndex(resource, searchParameter, passages.Count, chunks.Count));
                    passages.AddRange(chunks);
                }
            }

            if (pendingIndices.Count == 0)
            {
                _logger.LogInformation("Vector indexing found no text to embed across {ResourceCount} resource(s); embedding endpoint not invoked.", resources.Count);
                return;
            }

            _logger.LogInformation("Vector indexing invoking embedding endpoint for {PassageCount} passage(s) across {IndexCount} search-parameter target(s).", passages.Count, pendingIndices.Count);
            IReadOnlyList<float[]> embeddings = await _embeddingClient.GenerateEmbeddingsAsync(passages.Select(passage => passage.Text).ToList(), cancellationToken);
            if (embeddings.Count != passages.Count)
            {
                throw new InvalidOperationException("The embedding service returned a different number of vectors than passages.");
            }

            short embeddingModelId = await _embeddingModelRegistry.GetEmbeddingModelIdAsync(cancellationToken);
            foreach (PendingVectorIndex pendingIndex in pendingIndices)
            {
                var chunks = new List<VectorSearchChunk>(pendingIndex.PassageCount);
                for (int chunkOrdinal = 0; chunkOrdinal < pendingIndex.PassageCount; chunkOrdinal++)
                {
                    int passageIndex = pendingIndex.FirstPassageIndex + chunkOrdinal;
                    float[] embedding = embeddings[passageIndex];
                    if (embedding.Length != _embeddingClient.Dimensions)
                    {
                        throw new InvalidOperationException($"The embedding service returned a vector with {embedding.Length} dimensions; expected {_embeddingClient.Dimensions}.");
                    }

                    VectorTextSource passage = passages[passageIndex];
                    byte[] sourceTextHash = SHA256.HashData(Encoding.UTF8.GetBytes(passage.Text));
                    chunks.Add(new VectorSearchChunk(
                        chunkOrdinal,
                        passage.Text,
                        sourceTextHash,
                        embedding,
                        passage.ResourceType,
                        passage.ResourceId,
                        passage.ResourceVersion,
                        passage.Path));
                }

                entriesByResource[pendingIndex.Resource].Add(
                    new VectorSearchIndexEntry(pendingIndex.SearchParameter, embeddingModelId, chunks));
            }

            foreach (KeyValuePair<ResourceWrapper, List<VectorSearchIndexEntry>> resourceEntries in entriesByResource)
            {
                resourceEntries.Key.UpdateVectorSearchIndices(resourceEntries.Value);
            }
        }

        private static List<string> GetExtractedValues(ResourceWrapper resource, SearchParameterInfo searchParameter)
        {
            var values = new List<string>();
            foreach (SearchIndexEntry searchIndex in resource.SearchIndices ?? Array.Empty<SearchIndexEntry>())
            {
                if (searchIndex.SearchParameter.Url != searchParameter.Url)
                {
                    continue;
                }

                if (searchIndex.Value is not StringSearchValue stringValue)
                {
                    throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must extract string values.");
                }

                values.Add(stringValue.String);
            }

            return values;
        }

        private static IReadOnlyList<VectorTextSource> ApplyExtractionPolicy(
            VectorTextExtractionPolicy extractionPolicy,
            IReadOnlyList<VectorTextSource> extractedValues)
        {
            if (extractedValues.Count == 0)
            {
                return Array.Empty<VectorTextSource>();
            }

            return extractionPolicy switch
            {
                VectorTextExtractionPolicy.FirstValue => new[] { extractedValues[0] },
                VectorTextExtractionPolicy.Concatenate => extractedValues
                    .GroupBy(value => (value.ResourceType, value.ResourceId, value.ResourceVersion, value.Path))
                    .Select(group => new VectorTextSource(
                        string.Join(ConcatenatedValueSeparator, group.Select(value => value.Text)),
                        group.Key.ResourceType,
                        group.Key.ResourceId,
                        group.Key.ResourceVersion,
                        group.Key.Path))
                    .ToList(),
                VectorTextExtractionPolicy.PerValueRow => extractedValues,
                _ => throw new InvalidOperationException($"Unsupported vector text extraction policy '{extractionPolicy}'."),
            };
        }

        private sealed class PendingVectorIndex
        {
            public PendingVectorIndex(
                ResourceWrapper resource,
                SearchParameterInfo searchParameter,
                int firstPassageIndex,
                int passageCount)
            {
                Resource = resource;
                SearchParameter = searchParameter;
                FirstPassageIndex = firstPassageIndex;
                PassageCount = passageCount;
            }

            public ResourceWrapper Resource { get; }

            public SearchParameterInfo SearchParameter { get; }

            public int FirstPassageIndex { get; }

            public int PassageCount { get; }
        }
    }
}
