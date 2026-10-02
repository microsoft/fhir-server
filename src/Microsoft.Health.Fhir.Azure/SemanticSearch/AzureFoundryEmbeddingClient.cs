// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Core;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using OpenAI.Embeddings;

namespace Microsoft.Health.Fhir.Azure.SemanticSearch
{
    /// <summary>
    /// An <see cref="IEmbeddingClient"/> that calls an external Azure OpenAI / Foundry embedding deployment.
    /// It authenticates with a <see cref="TokenCredential"/> (managed identity in production, developer sign-in
    /// locally), so no API key is ever stored.
    /// </summary>
    public sealed class AzureFoundryEmbeddingClient : IEmbeddingClient
    {
        internal const int MaxInputTokens = VectorSearchConfiguration.MaxEmbeddingInputTokens;
        internal const int MaxBatchInputCount = 2048;
        internal const int MaxBatchTokenCount = 300000;

        private readonly EmbeddingClient _embeddingClient;
        private readonly ITextChunker _textChunker;
        private readonly int _maxBatchInputCount;
        private readonly int _maxBatchTokenCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureFoundryEmbeddingClient"/> class.
        /// </summary>
        /// <param name="configuration">The embedding endpoint configuration.</param>
        /// <param name="tokenCredential">The credential used to authenticate to the endpoint.</param>
        /// <param name="textChunker">The model-compatible tokenizer used to enforce provider limits.</param>
        public AzureFoundryEmbeddingClient(
            VectorSearchEmbeddingConfiguration configuration,
            TokenCredential tokenCredential,
            ITextChunker textChunker)
            : this(
                configuration,
                tokenCredential,
                textChunker,
                new AzureOpenAIClientOptions(),
                MaxBatchInputCount,
                MaxBatchTokenCount)
        {
        }

        internal AzureFoundryEmbeddingClient(
            VectorSearchEmbeddingConfiguration configuration,
            TokenCredential tokenCredential,
            ITextChunker textChunker,
            AzureOpenAIClientOptions clientOptions,
            int maxBatchInputCount,
            int maxBatchTokenCount)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(configuration.Endpoint, nameof(configuration.Endpoint));
            EnsureArg.IsNotNullOrWhiteSpace(configuration.DeploymentName, nameof(configuration.DeploymentName));
            EnsureArg.IsGt(configuration.Dimensions, 0, nameof(configuration.Dimensions));
            EnsureArg.IsNotNull(tokenCredential, nameof(tokenCredential));
            _textChunker = EnsureArg.IsNotNull(textChunker, nameof(textChunker));
            EnsureArg.IsNotNull(clientOptions, nameof(clientOptions));
            _maxBatchInputCount = EnsureArg.IsGt(maxBatchInputCount, 0, nameof(maxBatchInputCount));
            _maxBatchTokenCount = EnsureArg.IsGt(maxBatchTokenCount, 0, nameof(maxBatchTokenCount));

            Dimensions = configuration.Dimensions;

            var azureClient = new AzureOpenAIClient(configuration.Endpoint, tokenCredential, clientOptions);
            _embeddingClient = azureClient.GetEmbeddingClient(configuration.DeploymentName);
        }

        /// <inheritdoc />
        public int Dimensions { get; }

        /// <inheritdoc />
        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(texts, nameof(texts));

            if (texts.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            var embeddings = new List<float[]>(texts.Count);
            var batch = new List<string>(Math.Min(texts.Count, _maxBatchInputCount));
            int batchTokenCount = 0;

            foreach (string text in texts)
            {
                int inputTokenCount = _textChunker.CountTokens(text);
                if (inputTokenCount > MaxInputTokens)
                {
                    throw new InvalidOperationException($"Embedding input contains {inputTokenCount} tokens; the provider limit is {MaxInputTokens} tokens per input.");
                }

                if (batch.Count > 0 &&
                    (batch.Count == _maxBatchInputCount || batchTokenCount + inputTokenCount > _maxBatchTokenCount))
                {
                    embeddings.AddRange(await GenerateBatchAsync(batch, cancellationToken));
                    batch.Clear();
                    batchTokenCount = 0;
                }

                batch.Add(text);
                batchTokenCount += inputTokenCount;
            }

            if (batch.Count > 0)
            {
                embeddings.AddRange(await GenerateBatchAsync(batch, cancellationToken));
            }

            return embeddings;
        }

        private async Task<IReadOnlyList<float[]>> GenerateBatchAsync(List<string> texts, CancellationToken cancellationToken)
        {
            var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };
            ClientResult<OpenAIEmbeddingCollection> response = await _embeddingClient.GenerateEmbeddingsAsync(texts, options, cancellationToken);
            List<float[]> embeddings = response.Value
                .Select(embedding => embedding.ToFloats().ToArray())
                .ToList();

            if (embeddings.Count != texts.Count)
            {
                throw new InvalidOperationException($"The embedding service returned {embeddings.Count} vectors for {texts.Count} inputs.");
            }

            return embeddings;
        }
    }
}
