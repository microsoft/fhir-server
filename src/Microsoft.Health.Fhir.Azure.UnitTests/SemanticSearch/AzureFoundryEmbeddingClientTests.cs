// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Health.Fhir.Azure.SemanticSearch;
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

            var configuration = new EmbeddingConfiguration
            {
                Endpoint = new Uri(endpoint),
                DeploymentName = deployment,
                Dimensions = 1536,
            };

            var client = new AzureFoundryEmbeddingClient(configuration, new DefaultAzureCredential());

            var embeddings = await client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);

            Assert.Single(embeddings);
            Assert.Equal(configuration.Dimensions, embeddings[0].Length);
        }
    }
}
