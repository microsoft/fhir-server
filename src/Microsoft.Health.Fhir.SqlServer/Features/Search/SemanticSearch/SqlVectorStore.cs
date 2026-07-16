// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// A prototype <see cref="IVectorStore"/> that writes passage vectors to the <c>VectorSearchParam</c> table
    /// with a direct parameterized insert, passing each vector as a JSON array cast to the SQL <c>VECTOR</c> type.
    /// The production path writes these rows through the merge pipeline (see the design doc, section 11).
    /// </summary>
    public sealed class SqlVectorStore : IVectorStore
    {
        private const string InsertChunk = @"
INSERT INTO dbo.VectorSearchParam
    (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, Embedding)
VALUES
    (@ResourceTypeId, @ResourceSurrogateId, @SearchParamId, @ChunkOrdinal, @EmbeddingModelId, @SourceTextHash, CAST(@Embedding AS VECTOR(1536)));";

        // The candidate ids arrive as one comma-delimited bound parameter and are split server-side, so the query text
        // stays a compile-time constant (no user input is ever concatenated into it) while every value is parameterized.
        private const string SearchChunks = @"
SELECT TOP (@MaxResults)
       v.ResourceSurrogateId,
       v.ChunkOrdinal,
       VECTOR_DISTANCE('cosine', v.Embedding, CAST(@QueryEmbedding AS VECTOR(1536))) AS Distance
FROM   dbo.VectorSearchParam AS v
WHERE  v.ResourceTypeId   = @ResourceTypeId
  AND  v.EmbeddingModelId = @EmbeddingModelId
  AND  v.ResourceSurrogateId IN (SELECT CAST(value AS BIGINT) FROM STRING_SPLIT(@CandidateIds, ','))
ORDER BY Distance;";

        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlVectorStore"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string for the FHIR SQL database.</param>
        public SqlVectorStore(string connectionString)
        {
            EnsureArg.IsNotNullOrWhiteSpace(connectionString, nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <inheritdoc />
        public async Task StoreAsync(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            short embeddingModelId,
            IReadOnlyList<VectorSearchChunk> chunks,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(chunks, nameof(chunks));

            if (chunks.Count == 0)
            {
                return;
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            foreach (VectorSearchChunk chunk in chunks)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = InsertChunk;

                command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
                command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
                command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = searchParamId;
                command.Parameters.Add("@ChunkOrdinal", SqlDbType.Int).Value = chunk.ChunkOrdinal;
                command.Parameters.Add("@EmbeddingModelId", SqlDbType.SmallInt).Value = embeddingModelId;

                byte[] hash = ToArray(chunk.SourceTextHash);
                command.Parameters.Add("@SourceTextHash", SqlDbType.Binary, hash.Length).Value = hash;
                command.Parameters.Add("@Embedding", SqlDbType.NVarChar, -1).Value = FormatVector(chunk.Embedding);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            short resourceTypeId,
            short embeddingModelId,
            IReadOnlyList<float> queryEmbedding,
            IReadOnlyList<long> candidateResourceSurrogateIds,
            int maxResults,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(queryEmbedding, nameof(queryEmbedding));
            EnsureArg.IsNotNull(candidateResourceSurrogateIds, nameof(candidateResourceSurrogateIds));
            EnsureArg.IsGt(maxResults, 0, nameof(maxResults));

            // Nothing passed the structured filter, so there is nothing to rank.
            if (candidateResourceSurrogateIds.Count == 0)
            {
                return Array.Empty<VectorSearchResult>();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = SearchChunks;

            command.Parameters.Add("@MaxResults", SqlDbType.Int).Value = maxResults;
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@EmbeddingModelId", SqlDbType.SmallInt).Value = embeddingModelId;
            command.Parameters.Add("@QueryEmbedding", SqlDbType.NVarChar, -1).Value = FormatVector(queryEmbedding);
            command.Parameters.Add("@CandidateIds", SqlDbType.NVarChar, -1).Value = string.Join(",", candidateResourceSurrogateIds);

            var results = new List<VectorSearchResult>();

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long resourceSurrogateId = reader.GetInt64(0);
                int chunkOrdinal = reader.GetInt32(1);
                double distance = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);

                // VECTOR_DISTANCE('cosine', ...) is 0 (identical) to 2 (opposite); map it to a 0..1 relevance score where higher is better.
                float score = (float)(1.0 - (distance / 2.0));

                results.Add(new VectorSearchResult(resourceSurrogateId, chunkOrdinal, score));
            }

            return results;
        }

        private static byte[] ToArray(IReadOnlyList<byte> source)
        {
            var result = new byte[source.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        private static string FormatVector(IReadOnlyList<float> embedding)
        {
            var builder = new StringBuilder((embedding.Count * 8) + 2);
            builder.Append('[');

            for (int i = 0; i < embedding.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(embedding[i].ToString("R", CultureInfo.InvariantCulture));
            }

            builder.Append(']');
            return builder.ToString();
        }
    }
}
