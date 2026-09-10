// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.SemanticSearch;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class AzureFoundryEmbeddingClientTests
    {
        // Opt-in integration test: it only runs when the endpoint is configured through environment variables,
        // so CI (which has neither credentials nor network access to the endpoint) stays offline.
        // To run locally: az login, then set FHIR_TEST_EMBEDDING_ENDPOINT and FHIR_TEST_EMBEDDING_DEPLOYMENT.
        [Fact]
        public async Task GivenAConfiguredEndpoint_WhenEmbeddingText_ThenAVectorOfTheConfiguredDimensionsIsReturned()
        {
            string endpoint = Environment.GetEnvironmentVariable("FHIR_TEST_EMBEDDING_ENDPOINT");
            string deployment = Environment.GetEnvironmentVariable("FHIR_TEST_EMBEDDING_DEPLOYMENT");

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
            {
                return;
            }

            var configuration = new VectorSearchEmbeddingConfiguration
            {
                Endpoint = new Uri(endpoint),
                DeploymentName = deployment,
                Dimensions = 1536,
            };

            var client = new AzureFoundryEmbeddingClient(configuration, new DefaultAzureCredential(), CreateTextChunker(configuration));

            var embeddings = await client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);

            Assert.Single(embeddings);
            Assert.Equal(configuration.Dimensions, embeddings[0].Length);
        }

        [Fact]
        public async Task GivenInputsExceedingProviderBatchCount_WhenEmbedding_ThenRequestsAreBatchedAndResultsStayOrdered()
        {
            var handler = new RecordingEmbeddingHandler();
            AzureFoundryEmbeddingClient client = CreateClient(handler, maxBatchInputCount: 3, maxBatchTokenCount: 100);
            string[] texts = Enumerable.Range(0, 8).Select(index => $"input {index}").ToArray();

            IReadOnlyList<float[]> embeddings = await client.GenerateEmbeddingsAsync(texts, CancellationToken.None);

            Assert.Equal(new[] { 3, 3, 2 }, handler.RequestInputCounts);
            Assert.Equal(Enumerable.Range(0, 8).Select(index => (float)index), embeddings.Select(embedding => embedding[0]));
        }

        [Fact]
        public async Task GivenInputsExceedingProviderAggregateTokens_WhenEmbedding_ThenRequestsRespectTheTokenLimit()
        {
            var handler = new RecordingEmbeddingHandler();
            AzureFoundryEmbeddingClient client = CreateClient(handler, maxBatchInputCount: 100, maxBatchTokenCount: 5);

            await client.GenerateEmbeddingsAsync(new[] { " one two", " three four", " five six" }, CancellationToken.None);

            Assert.Equal(new[] { 2, 1 }, handler.RequestInputCounts);
        }

        [Fact]
        public async Task GivenAnInputExceedingTheProviderPerInputLimit_WhenEmbedding_ThenItFailsBeforeSendingARequest()
        {
            var handler = new RecordingEmbeddingHandler();
            AzureFoundryEmbeddingClient client = CreateClient(handler);
            string text = string.Concat(Enumerable.Repeat(" token", AzureFoundryEmbeddingClient.MaxInputTokens + 1));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GenerateEmbeddingsAsync(new[] { text }, CancellationToken.None));

            Assert.Empty(handler.RequestInputCounts);
        }

        private static AzureFoundryEmbeddingClient CreateClient(
            RecordingEmbeddingHandler handler,
            int maxBatchInputCount = AzureFoundryEmbeddingClient.MaxBatchInputCount,
            int maxBatchTokenCount = AzureFoundryEmbeddingClient.MaxBatchTokenCount)
        {
            var configuration = new VectorSearchEmbeddingConfiguration
            {
                Endpoint = new Uri("https://semantic-search.test"),
                DeploymentName = "text-embedding-3-small",
                ModelName = "text-embedding-3-small",
                Dimensions = 2,
            };
            var options = new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            };

            return new AzureFoundryEmbeddingClient(
                configuration,
                new StaticTokenCredential(),
                CreateTextChunker(configuration),
                options,
                maxBatchInputCount,
                maxBatchTokenCount);
        }

        private static TextChunker CreateTextChunker(VectorSearchEmbeddingConfiguration embedding)
        {
            return new TextChunker(
                Options.Create(
                    new VectorSearchConfiguration
                    {
                        Embedding = embedding,
                    }));
        }

        private sealed class StaticTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return new AccessToken("test-token", DateTimeOffset.MaxValue);
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
            }
        }

        private sealed class RecordingEmbeddingHandler : HttpMessageHandler
        {
            private int _nextEmbeddingIndex;

            public List<int> RequestInputCounts { get; } = new List<int>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string requestJson = await request.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
                JsonElement inputs = requestDocument.RootElement.GetProperty("input");
                RequestInputCounts.Add(inputs.GetArrayLength());

                var data = new List<object>();
                foreach (JsonElement input in inputs.EnumerateArray())
                {
                    float[] embedding = new[] { (float)_nextEmbeddingIndex++, 1f };
                    data.Add(new
                    {
                        embedding = Convert.ToBase64String(MemoryMarshal.AsBytes(embedding.AsSpan())),
                        index = data.Count,
                        @object = "embedding",
                    });
                }

                string responseJson = JsonSerializer.Serialize(
                    new
                    {
                        data,
                        model = "text-embedding-3-small",
                        @object = "list",
                        usage = new { prompt_tokens = inputs.GetArrayLength(), total_tokens = inputs.GetArrayLength() },
                    });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                    RequestMessage = request,
                };
            }
        }
    }
}
