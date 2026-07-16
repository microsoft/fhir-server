// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlVectorStoreTests
    {
        private const short TestResourceTypeId = 100;
        private const short TestSearchParamId = 1;
        private const short TestEmbeddingModelId = 1;

        // Opt-in integration test: it only runs when the database is configured through an environment variable,
        // so CI (which has neither credentials nor network access to the database) stays offline.
        // To run locally: az login, then set FHIR_TEST_SQL_CONNECTIONSTRING to the FHIR database connection string
        // (for example "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<db>;Authentication=Active Directory Default;Encrypt=True;").
        [Fact]
        public async Task GivenChunks_WhenStored_ThenTheyArePersistedToVectorSearchParam()
        {
            string connectionString = Environment.GetEnvironmentVariable("FHIR_TEST_SQL_CONNECTIONSTRING");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            long resourceSurrogateId = DateTime.UtcNow.Ticks;

            float[] embedding = await CreateEmbeddingAsync();
            var chunks = new List<VectorSearchChunk>
            {
                new VectorSearchChunk(0, new byte[32], embedding),
                new VectorSearchChunk(1, new byte[32], embedding),
            };

            var store = new SqlVectorStore(connectionString);

            try
            {
                await store.StoreAsync(TestResourceTypeId, resourceSurrogateId, TestSearchParamId, TestEmbeddingModelId, chunks, CancellationToken.None);

                int count = await CountAsync(connectionString, resourceSurrogateId);
                Assert.Equal(2, count);
            }
            finally
            {
                await DeleteAsync(connectionString, resourceSurrogateId);
            }
        }

        [Fact]
        public async Task GivenStoredVectors_WhenSearching_ThenTheClosestPassageRanksFirst()
        {
            string connectionString = Environment.GetEnvironmentVariable("FHIR_TEST_SQL_CONNECTIONSTRING");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            long surrogateChestPain = DateTime.UtcNow.Ticks;
            long surrogateFracture = surrogateChestPain + 1;

            var client = new DeterministicEmbeddingClient(dimensions: 1536);
            IReadOnlyList<float[]> vectors = await client.GenerateEmbeddingsAsync(new[] { "chest pain", "fractured femur" }, CancellationToken.None);
            float[] chestPain = vectors[0];
            float[] fracture = vectors[1];

            var store = new SqlVectorStore(connectionString);

            try
            {
                await store.StoreAsync(TestResourceTypeId, surrogateChestPain, TestSearchParamId, TestEmbeddingModelId, new[] { new VectorSearchChunk(0, new byte[32], chestPain) }, CancellationToken.None);
                await store.StoreAsync(TestResourceTypeId, surrogateFracture, TestSearchParamId, TestEmbeddingModelId, new[] { new VectorSearchChunk(0, new byte[32], fracture) }, CancellationToken.None);

                IReadOnlyList<VectorSearchResult> results = await store.SearchAsync(
                    TestResourceTypeId,
                    TestEmbeddingModelId,
                    chestPain,
                    new[] { surrogateChestPain, surrogateFracture },
                    maxResults: 2,
                    CancellationToken.None);

                Assert.Equal(2, results.Count);
                Assert.Equal(surrogateChestPain, results[0].ResourceSurrogateId);
                Assert.True(results[0].Score >= results[1].Score);
            }
            finally
            {
                await DeleteAsync(connectionString, surrogateChestPain);
                await DeleteAsync(connectionString, surrogateFracture);
            }
        }

        private static async Task<float[]> CreateEmbeddingAsync()
        {
            var client = new DeterministicEmbeddingClient(dimensions: 1536);
            IReadOnlyList<float[]> vectors = await client.GenerateEmbeddingsAsync(new[] { "chest pain" }, CancellationToken.None);
            return vectors[0];
        }

        private static async Task<int> CountAsync(string connectionString, long resourceSurrogateId)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceTypeId = @rt AND ResourceSurrogateId = @rid;";
            command.Parameters.AddWithValue("@rt", TestResourceTypeId);
            command.Parameters.AddWithValue("@rid", resourceSurrogateId);

            return (int)await command.ExecuteScalarAsync();
        }

        private static async Task DeleteAsync(string connectionString, long resourceSurrogateId)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM dbo.VectorSearchParam WHERE ResourceTypeId = @rt AND ResourceSurrogateId = @rid;";
            command.Parameters.AddWithValue("@rt", TestResourceTypeId);
            command.Parameters.AddWithValue("@rid", resourceSurrogateId);

            await command.ExecuteNonQueryAsync();
        }
    }
}
