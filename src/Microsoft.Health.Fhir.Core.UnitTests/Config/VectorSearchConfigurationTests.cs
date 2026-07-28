// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class VectorSearchConfigurationTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenCreated_ThenSafeDefaultsAreUsed()
        {
            // Arrange and Act
            var configuration = new VectorSearchConfiguration();

            // Assert
            Assert.False(configuration.Enabled);
            Assert.Null(configuration.Embedding.Endpoint);
            Assert.Null(configuration.Embedding.DeploymentName);
            Assert.Equal("text-embedding-3-small", configuration.Embedding.ModelName);
            Assert.Null(configuration.Embedding.ModelVersion);
            Assert.Equal(VectorSearchConfiguration.SupportedDimensions, configuration.Embedding.Dimensions);
            Assert.Equal(VectorSearchIndexingMode.Synchronous, configuration.Indexing.Mode);
            Assert.Empty(configuration.Indexing.EnabledSearchParameters);
            Assert.Equal(800, configuration.Indexing.ChunkSizeTokens);
            Assert.Equal(100, configuration.Indexing.ChunkOverlapTokens);
            Assert.Equal(10, configuration.Query.DefaultCount);
            Assert.Equal(50, configuration.Query.MaxCount);
            Assert.Equal(100, configuration.Query.CandidateCount);
            Assert.Equal(VectorSearchConfiguration.SupportedDistanceMetric, configuration.Query.DistanceMetric);
        }

        [Fact]
        public void GivenDisabledConfiguration_WhenInvalidValuesArePresent_ThenValidationIsSkipped()
        {
            // Arrange
            var configuration = new VectorSearchConfiguration
            {
                Enabled = false,
                Embedding = null,
                Indexing = null,
                Query = null,
            };

            // Act
            Exception exception = Record.Exception(configuration.Validate);

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void GivenCompleteEnabledConfiguration_WhenValidated_ThenValidationSucceeds()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();

            // Act
            Exception exception = Record.Exception(configuration.Validate);

            // Assert
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("/relative")]
        [InlineData("http://embedding.example.com")]
        public void GivenEnabledConfigurationWithInvalidEndpoint_WhenValidated_ThenValidationFails(string endpoint)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.Endpoint = endpoint == null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute);

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GivenEnabledConfigurationWithMissingDeployment_WhenValidated_ThenValidationFails(string deploymentName)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.DeploymentName = deploymentName;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GivenEnabledConfigurationWithMissingModel_WhenValidated_ThenValidationFails(string modelName)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelName = modelName;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GivenEnabledConfigurationWithMissingModelVersion_WhenValidated_ThenValidationFails(string modelVersion)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelVersion = modelVersion;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3072)]
        public void GivenEnabledConfigurationWithUnsupportedDimensions_WhenValidated_ThenValidationFails(int dimensions)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.Dimensions = dimensions;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithoutEmbeddingSettings_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding = null;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithoutIndexingSettings_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing = null;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithUnsupportedIndexingMode_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.Mode = (VectorSearchIndexingMode)int.MaxValue;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithoutSearchParameters_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.EnabledSearchParameters.Clear();

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithRelativeSearchParameterUri_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.EnabledSearchParameters.Clear();
            configuration.Indexing.EnabledSearchParameters.Add(new Uri("relative", UriKind.Relative));

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithDuplicateSearchParameterUri_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.EnabledSearchParameters.Add(configuration.Indexing.EnabledSearchParameters[0]);

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GivenEnabledConfigurationWithInvalidChunkSize_WhenValidated_ThenValidationFails(int chunkSizeTokens)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.ChunkSizeTokens = chunkSizeTokens;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(800)]
        [InlineData(801)]
        public void GivenEnabledConfigurationWithInvalidChunkOverlap_WhenValidated_ThenValidationFails(int chunkOverlapTokens)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.ChunkOverlapTokens = chunkOverlapTokens;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithoutQuerySettings_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query = null;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GivenEnabledConfigurationWithInvalidDefaultCount_WhenValidated_ThenValidationFails(int defaultCount)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.DefaultCount = defaultCount;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithMaximumBelowDefault_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.DefaultCount = 10;
            configuration.Query.MaxCount = 9;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithCandidateCountBelowMaximum_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.CandidateCount = configuration.Query.MaxCount - 1;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Fact]
        public void GivenEnabledConfigurationWithUnsupportedDistanceMetric_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.DistanceMetric = "euclidean";

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        private static VectorSearchConfiguration CreateValidConfiguration()
        {
            var configuration = new VectorSearchConfiguration
            {
                Enabled = true,
                Embedding = new VectorSearchEmbeddingConfiguration
                {
                    Endpoint = new Uri("https://embedding.example.com"),
                    DeploymentName = "embedding-deployment",
                    ModelName = "text-embedding-3-small",
                    ModelVersion = "1",
                    Dimensions = VectorSearchConfiguration.SupportedDimensions,
                },
                Indexing = new VectorSearchIndexingConfiguration(),
            };

            configuration.Indexing.EnabledSearchParameters.Add(new Uri("https://example.com/fhir/SearchParameter/document-reference-content-vector"));
            return configuration;
        }
    }
}
