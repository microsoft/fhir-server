// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Verifies the embedding model registry against the case-sensitive registry columns.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SqlEmbeddingModelRegistryTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;

        /// <summary>
        /// Initializes tests using an isolated SQL storage fixture.
        /// </summary>
        /// <param name="fixture">The SQL storage fixture.</param>
        public SqlEmbeddingModelRegistryTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Verifies a restart with a differently cased metric resolves the model that was already registered.
        /// </summary>
        [Fact]
        public async Task GivenRegisteredModel_WhenDistanceMetricDiffersOnlyByCase_ThenTheRegisteredModelIsResolved()
        {
            // Arrange
            string modelName = Guid.NewGuid().ToString("N");
            using SqlEmbeddingModelRegistry registeredRegistry = CreateRegistry(modelName, "cosine", VectorSearchConfiguration.SupportedDimensions);
            using SqlEmbeddingModelRegistry restartedRegistry = CreateRegistry(modelName, "COSINE", VectorSearchConfiguration.SupportedDimensions);
            short registeredId = await registeredRegistry.GetEmbeddingModelIdAsync(CancellationToken.None);

            // Act
            short resolvedId = await restartedRegistry.GetEmbeddingModelIdAsync(CancellationToken.None);

            // Assert
            Assert.Equal(registeredId, resolvedId);
        }

        /// <summary>
        /// Verifies new registry rows store the metric in the canonical lowercase form.
        /// </summary>
        [Fact]
        public async Task GivenUppercaseDistanceMetric_WhenRegisteringModel_ThenTheMetricIsStoredLowercase()
        {
            // Arrange
            using SqlEmbeddingModelRegistry registry = CreateRegistry(Guid.NewGuid().ToString("N"), "COSINE", VectorSearchConfiguration.SupportedDimensions);

            // Act
            short embeddingModelId = await registry.GetEmbeddingModelIdAsync(CancellationToken.None);

            // Assert
            Assert.Equal("cosine", await GetStoredDistanceMetricAsync(embeddingModelId));
        }

        /// <summary>
        /// Verifies metadata that genuinely differs from the registered row is still rejected.
        /// </summary>
        [Fact]
        public async Task GivenRegisteredModel_WhenDimensionDiffers_ThenResolutionIsRejected()
        {
            // Arrange
            string modelName = Guid.NewGuid().ToString("N");
            using SqlEmbeddingModelRegistry registeredRegistry = CreateRegistry(modelName, "cosine", VectorSearchConfiguration.SupportedDimensions);
            using SqlEmbeddingModelRegistry mismatchedRegistry = CreateRegistry(modelName, "cosine", VectorSearchConfiguration.SupportedDimensions / 2);
            await registeredRegistry.GetEmbeddingModelIdAsync(CancellationToken.None);

            // Act
            Func<Task> resolve = () => mismatchedRegistry.GetEmbeddingModelIdAsync(CancellationToken.None);

            // Assert
            SqlException exception = await Assert.ThrowsAsync<SqlException>(resolve);
            Assert.Equal(50000, exception.Number);
        }

        private SqlEmbeddingModelRegistry CreateRegistry(string modelName, string distanceMetric, int dimensions)
        {
            var configuration = new VectorSearchConfiguration
            {
                Embedding = new VectorSearchEmbeddingConfiguration
                {
                    ModelName = modelName,
                    ModelVersion = "1",
                    Dimensions = dimensions,
                },
                Query = new VectorSearchQueryConfiguration
                {
                    DistanceMetric = distanceMetric,
                },
            };

            return new SqlEmbeddingModelRegistry(_fixture.TestConnectionString, Options.Create(configuration));
        }

        private async Task<string> GetStoredDistanceMetricAsync(short embeddingModelId)
        {
            await using var connection = new SqlConnection(_fixture.TestConnectionString);
            await connection.OpenAsync(CancellationToken.None);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DistanceMetric FROM dbo.EmbeddingModel WHERE EmbeddingModelId = @EmbeddingModelId";
            command.Parameters.Add("@EmbeddingModelId", SqlDbType.SmallInt).Value = embeddingModelId;

            return (string)await command.ExecuteScalarAsync(CancellationToken.None);
        }
    }
}
