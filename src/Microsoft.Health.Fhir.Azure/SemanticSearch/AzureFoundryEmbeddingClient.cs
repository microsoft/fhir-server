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
        private readonly EmbeddingClient _embeddingClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureFoundryEmbeddingClient"/> class.
        /// </summary>
        /// <param name="configuration">The embedding endpoint configuration.</param>
        /// <param name="tokenCredential">The credential used to authenticate to the endpoint.</param>
        public AzureFoundryEmbeddingClient(EmbeddingConfiguration configuration, TokenCredential tokenCredential)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(configuration.Endpoint, nameof(configuration.Endpoint));
            EnsureArg.IsNotNullOrWhiteSpace(configuration.DeploymentName, nameof(configuration.DeploymentName));
            EnsureArg.IsGt(configuration.Dimensions, 0, nameof(configuration.Dimensions));
            EnsureArg.IsNotNull(tokenCredential, nameof(tokenCredential));

            Dimensions = configuration.Dimensions;

            var azureClient = new AzureOpenAIClient(configuration.Endpoint, tokenCredential);
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

            var options = new EmbeddingGenerationOptions { Dimensions = Dimensions };

            ClientResult<OpenAIEmbeddingCollection> response = await _embeddingClient.GenerateEmbeddingsAsync(texts, options, cancellationToken);

            return response.Value
                .Select(embedding => embedding.ToFloats().ToArray())
                .ToList();
        }
    }
}
