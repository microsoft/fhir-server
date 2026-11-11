// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
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
            Assert.False(configuration.Embedding.SendDimensionsParameter);
            Assert.Equal(VectorSearchIndexingMode.Synchronous, configuration.Indexing.Mode);
            Assert.Equal(800, configuration.Indexing.ChunkSizeTokens);
            Assert.Equal(100, configuration.Indexing.ChunkOverlapTokens);
            Assert.Equal(VectorSearchConfiguration.SupportedDistanceMetric, configuration.Query.DistanceMetric);
            Assert.Equal(VectorSearchApproximateMode.Disabled, configuration.Query.ApproximateSearch);
            Assert.Equal(10, configuration.Query.ApproximateCandidateMultiplier);
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

        [Fact]
        public void GivenEnabledConfigurationWithModelIdentifiersAtColumnWidth_WhenValidated_ThenValidationSucceeds()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelName = new string('n', VectorSearchConfiguration.MaxModelNameLength);
            configuration.Embedding.ModelVersion = new string('v', VectorSearchConfiguration.MaxModelVersionLength);

            // Act
            Exception exception = Record.Exception(configuration.Validate);

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void GivenEnabledConfigurationWithModelNameBeyondColumnWidth_WhenValidated_ThenValidationFails()
        {
            // Arrange
            // The registry column would silently truncate the value, aliasing it with a different declared model.
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelName = new string('n', VectorSearchConfiguration.MaxModelNameLength + 1);

            // Act
            Action validate = configuration.Validate;

            // Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(validate);
            Assert.Contains("model name", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenEnabledConfigurationWithModelVersionBeyondColumnWidth_WhenValidated_ThenValidationFails()
        {
            // Arrange
            // Two versions that differ only after the column width would otherwise resolve to the same model row.
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelVersion = new string('v', VectorSearchConfiguration.MaxModelVersionLength + 1);

            // Act
            Action validate = configuration.Validate;

            // Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(validate);
            Assert.Contains("model version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("text-embedding-3-small ")]
        [InlineData(" text-embedding-3-small")]
        [InlineData("text-embedding-3-small\t")]
        [InlineData("text-embedding-3-sm\u00e4ll")]
        [InlineData("\u30c6\u30ad\u30b9\u30c8")]
        public void GivenEnabledConfigurationWithModelNameTheRegistryCannotStoreExactly_WhenValidated_ThenValidationFails(string modelName)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelName = modelName;

            // Act
            Action validate = configuration.Validate;

            // Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(validate);
            Assert.Contains("model name", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("1 ")]
        [InlineData(" 1")]
        [InlineData("1\t")]
        [InlineData("1\u00e9")]
        [InlineData("\u30d0\u30fc\u30b8\u30e7\u30f3")]
        public void GivenEnabledConfigurationWithModelVersionTheRegistryCannotStoreExactly_WhenValidated_ThenValidationFails(string modelVersion)
        {
            // Arrange
            // SQL Server ignores trailing spaces when comparing, and non-code-page characters are stored as '?', so these
            // values would alias a different declared version.
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Embedding.ModelVersion = modelVersion;

            // Act
            Action validate = configuration.Validate;

            // Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(validate);
            Assert.Contains("model version", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(3)]
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

        [Fact]
        public void GivenEnabledConfigurationWithChunkSizeAboveProviderLimit_WhenValidated_ThenValidationFails()
        {
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Indexing.ChunkSizeTokens = VectorSearchConfiguration.MaxEmbeddingInputTokens + 1;

            Action validate = configuration.Validate;

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

        [Fact]
        public void GivenEnabledConfigurationWithUnsupportedApproximateSearchMode_WhenValidated_ThenValidationFails()
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.ApproximateSearch = (VectorSearchApproximateMode)99;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(VectorSearchQueryConfiguration.MaximumApproximateCandidateMultiplier + 1)]
        public void GivenEnabledConfigurationWithInvalidApproximateCandidateMultiplier_WhenValidated_ThenValidationFails(int multiplier)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.ApproximateSearch = VectorSearchApproximateMode.Auto;
            configuration.Query.ApproximateCandidateMultiplier = multiplier;

            // Act
            Action validate = configuration.Validate;

            // Assert
            Assert.Throws<InvalidOperationException>(validate);
        }

        [Theory]
        [InlineData(VectorSearchApproximateMode.Disabled, 1)]
        [InlineData(VectorSearchApproximateMode.Auto, 1)]
        [InlineData(VectorSearchApproximateMode.Auto, VectorSearchQueryConfiguration.MaximumApproximateCandidateMultiplier)]
        public void GivenEnabledConfigurationWithApproximateSettingsAtTheirLimits_WhenValidated_ThenValidationSucceeds(
            VectorSearchApproximateMode mode,
            int multiplier)
        {
            // Arrange
            VectorSearchConfiguration configuration = CreateValidConfiguration();
            configuration.Query.ApproximateSearch = mode;
            configuration.Query.ApproximateCandidateMultiplier = multiplier;

            // Act
            Exception exception = Record.Exception(configuration.Validate);

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void GivenApproximateSearchConfigurationValues_WhenBoundFromConfiguration_ThenTheyAreApplied()
        {
            // Arrange
            IConfiguration source = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Query:ApproximateSearch"] = "Auto",
                    ["Query:ApproximateCandidateMultiplier"] = "25",
                })
                .Build();
            var configuration = new VectorSearchConfiguration();

            // Act
            source.Bind(configuration);

            // Assert
            Assert.Equal(VectorSearchApproximateMode.Auto, configuration.Query.ApproximateSearch);
            Assert.Equal(25, configuration.Query.ApproximateCandidateMultiplier);
        }

        [Fact]
        public void GivenSendDimensionsParameterValue_WhenBoundFromConfiguration_ThenItIsApplied()
        {
            // Arrange
            IConfiguration source = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Embedding:SendDimensionsParameter"] = "true",
                })
                .Build();
            var configuration = new VectorSearchConfiguration();

            // Act
            source.Bind(configuration);

            // Assert
            Assert.True(configuration.Embedding.SendDimensionsParameter);
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

            return configuration;
        }
    }
}
