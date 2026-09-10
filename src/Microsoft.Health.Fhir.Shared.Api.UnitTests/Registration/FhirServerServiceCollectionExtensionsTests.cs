// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class FhirServerServiceCollectionExtensionsTests
    {
        [Fact]
        public void GivenCompleteVectorSearchSettings_WhenFhirServerIsAdded_ThenSettingsAreBoundAndRegistered()
        {
            // Arrange
            IConfiguration configuration = BuildConfiguration(new Dictionary<string, string>
            {
                ["FhirServer:CoreFeatures:VectorSearch:Enabled"] = "true",
                ["FhirServer:CoreFeatures:VectorSearch:Embedding:Endpoint"] = "https://embedding.example.com",
                ["FhirServer:CoreFeatures:VectorSearch:Embedding:DeploymentName"] = "embedding-deployment",
                ["FhirServer:CoreFeatures:VectorSearch:Embedding:ModelName"] = "embedding-model",
                ["FhirServer:CoreFeatures:VectorSearch:Embedding:ModelVersion"] = "1",
                ["FhirServer:CoreFeatures:VectorSearch:Embedding:Dimensions"] = "1536",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:Mode"] = "Synchronous",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:ChunkSizeTokens"] = "600",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:ChunkOverlapTokens"] = "50",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:Pdf:MaximumFileSizeBytes"] = "5242880",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:Pdf:MaximumPageCount"] = "100",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:Pdf:MaximumExtractedCharacters"] = "250000",
                ["FhirServer:CoreFeatures:VectorSearch:Indexing:Pdf:ExtractionTimeout"] = "00:00:15",
                ["FhirServer:CoreFeatures:VectorSearch:Query:DefaultCount"] = "5",
                ["FhirServer:CoreFeatures:VectorSearch:Query:MaxCount"] = "25",
                ["FhirServer:CoreFeatures:VectorSearch:Query:CandidateCount"] = "125",
                ["FhirServer:CoreFeatures:VectorSearch:Query:DistanceMetric"] = "cosine",
            });
            var services = new ServiceCollection();

            // Act
            services.AddFhirServer(configuration);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            VectorSearchConfiguration vectorSearch = serviceProvider.GetRequiredService<IOptions<VectorSearchConfiguration>>().Value;

            // Assert
            Assert.True(vectorSearch.Enabled);
            Assert.Equal(new Uri("https://embedding.example.com"), vectorSearch.Embedding.Endpoint);
            Assert.Equal("embedding-deployment", vectorSearch.Embedding.DeploymentName);
            Assert.Equal("embedding-model", vectorSearch.Embedding.ModelName);
            Assert.Equal("1", vectorSearch.Embedding.ModelVersion);
            Assert.Equal(1536, vectorSearch.Embedding.Dimensions);
            Assert.Equal(VectorSearchIndexingMode.Synchronous, vectorSearch.Indexing.Mode);
            Assert.Equal(600, vectorSearch.Indexing.ChunkSizeTokens);
            Assert.Equal(50, vectorSearch.Indexing.ChunkOverlapTokens);
            Assert.Equal(5 * 1024 * 1024, vectorSearch.Indexing.Pdf.MaximumFileSizeBytes);
            Assert.Equal(100, vectorSearch.Indexing.Pdf.MaximumPageCount);
            Assert.Equal(250_000, vectorSearch.Indexing.Pdf.MaximumExtractedCharacters);
            Assert.Equal(TimeSpan.FromSeconds(15), vectorSearch.Indexing.Pdf.ExtractionTimeout);
            Assert.Equal(5, vectorSearch.Query.DefaultCount);
            Assert.Equal(25, vectorSearch.Query.MaxCount);
            Assert.Equal(125, vectorSearch.Query.CandidateCount);
            Assert.Equal("cosine", vectorSearch.Query.DistanceMetric);
        }

        [Fact]
        public void GivenIncompleteEnabledVectorSearchSettings_WhenFhirServerIsAdded_ThenStartupValidationFails()
        {
            // Arrange
            IConfiguration configuration = BuildConfiguration(new Dictionary<string, string>
            {
                ["FhirServer:CoreFeatures:VectorSearch:Enabled"] = "true",
            });
            var services = new ServiceCollection();

            // Act
            Action addFhirServer = () => services.AddFhirServer(configuration);

            // Assert
            Assert.Throws<InvalidOperationException>(addFhirServer);
        }

        private static IConfiguration BuildConfiguration(IDictionary<string, string> settings)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }
    }
}
