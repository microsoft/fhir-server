// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
    (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, Embedding)
VALUES
    (@ResourceTypeId, @ResourceSurrogateId, @SearchParamId, @ChunkOrdinal, @EmbeddingModelId, @ChunkText, @SourceTextHash, CAST(@Embedding AS VECTOR(1536)));";

        private const string DeleteResourceChunks = @"
DELETE FROM dbo.VectorSearchParam
WHERE ResourceTypeId = @ResourceTypeId
  AND ResourceSurrogateId = @ResourceSurrogateId
    AND SearchParamId = @SearchParamId;";

        // The candidate ids arrive as one comma-delimited bound parameter and are split server-side, so the query text
        // stays a compile-time constant (no user input is ever concatenated into it) while every value is parameterized.
        private const string SearchChunks = @"
WITH RankedChunks AS
(
    SELECT v.ResourceSurrogateId,
           v.ChunkOrdinal,
           v.ChunkText,
           v.SourceResourceTypeId,
           v.SourceResourceId,
           v.SourceResourceVersion,
           v.SourcePath,
           VECTOR_DISTANCE(@DistanceMetric, v.Embedding, CAST(@QueryEmbedding AS VECTOR(1536))) AS Distance,
           ROW_NUMBER() OVER
           (
               PARTITION BY v.ResourceSurrogateId
               ORDER BY VECTOR_DISTANCE(@DistanceMetric, v.Embedding, CAST(@QueryEmbedding AS VECTOR(1536))), v.ChunkOrdinal
           ) AS ChunkRank
    FROM dbo.VectorSearchParam AS v
    WHERE v.ResourceTypeId = @ResourceTypeId
      AND v.SearchParamId = @SearchParamId
      AND v.EmbeddingModelId = @EmbeddingModelId
      AND v.ResourceSurrogateId IN (SELECT CAST(value AS BIGINT) FROM STRING_SPLIT(@CandidateIds, ','))
),
BestResources AS
(
    SELECT ResourceSurrogateId,
           MIN(Distance) AS BestDistance
    FROM RankedChunks
    GROUP BY ResourceSurrogateId
),
SelectedResources AS
(
    SELECT TOP (@MaxResults)
           ResourceSurrogateId,
           BestDistance
    FROM BestResources
    ORDER BY BestDistance, ResourceSurrogateId
)
SELECT c.ResourceSurrogateId,
       c.ChunkOrdinal,
       c.ChunkText,
       c.SourceResourceTypeId,
       c.SourceResourceId,
       c.SourceResourceVersion,
       c.SourcePath,
       c.Distance
FROM RankedChunks AS c
INNER JOIN SelectedResources AS s
    ON s.ResourceSurrogateId = c.ResourceSurrogateId
WHERE c.ChunkRank <= @EvidenceCount
ORDER BY s.BestDistance, s.ResourceSurrogateId, c.ChunkRank;";

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

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (SqlCommand deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = DeleteResourceChunks;
                AddResourceParameters(deleteCommand, resourceTypeId, resourceSurrogateId, searchParamId, embeddingModelId);
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (VectorSearchChunk chunk in chunks)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = InsertChunk;

                AddResourceParameters(command, resourceTypeId, resourceSurrogateId, searchParamId, embeddingModelId);
                command.Parameters.Add("@ChunkOrdinal", SqlDbType.SmallInt).Value = chunk.ChunkOrdinal;
                command.Parameters.Add("@ChunkText", SqlDbType.NVarChar, -1).Value = chunk.ChunkText;

                byte[] hash = ToArray(chunk.SourceTextHash);
                command.Parameters.Add("@SourceTextHash", SqlDbType.Binary, hash.Length).Value = hash;
                command.Parameters.Add("@Embedding", SqlDbType.NVarChar, -1).Value = SqlVectorFormatter.Format(chunk.Embedding);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            short resourceTypeId,
            short searchParamId,
            short embeddingModelId,
            string distanceMetric,
            IReadOnlyList<float> queryEmbedding,
            IReadOnlyList<long> candidateResourceSurrogateIds,
            int maxResults,
            int evidenceCount,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(queryEmbedding, nameof(queryEmbedding));
            EnsureArg.IsNotNull(candidateResourceSurrogateIds, nameof(candidateResourceSurrogateIds));
            EnsureArg.IsNotNullOrWhiteSpace(distanceMetric, nameof(distanceMetric));
            EnsureArg.IsGt(maxResults, 0, nameof(maxResults));
            EnsureArg.IsGt(evidenceCount, 0, nameof(evidenceCount));

            // Nothing passed the structured filter, so there is nothing to rank.
            if (candidateResourceSurrogateIds.Count == 0)
            {
                return Array.Empty<VectorSearchHit>();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = SearchChunks;

            command.Parameters.Add("@MaxResults", SqlDbType.Int).Value = maxResults;
            command.Parameters.Add("@EvidenceCount", SqlDbType.Int).Value = evidenceCount;
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = searchParamId;
            command.Parameters.Add("@EmbeddingModelId", SqlDbType.SmallInt).Value = embeddingModelId;
            command.Parameters.Add("@DistanceMetric", SqlDbType.VarChar, 16).Value = distanceMetric;
            command.Parameters.Add("@QueryEmbedding", SqlDbType.NVarChar, -1).Value = SqlVectorFormatter.Format(queryEmbedding);
            command.Parameters.Add("@CandidateIds", SqlDbType.NVarChar, -1).Value = string.Join(",", candidateResourceSurrogateIds);

            var results = new List<VectorSearchHit>();

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long resourceSurrogateId = reader.GetInt64(0);
                int chunkOrdinal = reader.GetInt16(1);
                string chunkText = reader.GetString(2);
                short? sourceResourceTypeId = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetInt16(3);
                string sourceResourceId = await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4);
                string sourceResourceVersion = await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5);
                string sourcePath = await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetString(6);
                double distance = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture);

                // The supported cosine distance is 0 (identical) to 2 (opposite); map it to a 0..1 relevance score where higher is better.
                float score = (float)Math.Clamp(1.0 - (distance / 2.0), 0.0, 1.0);

                results.Add(new VectorSearchHit(
                    resourceSurrogateId,
                    chunkOrdinal,
                    chunkText,
                    score,
                    sourceResourceTypeId,
                    sourceResourceId,
                    sourceResourceVersion,
                    sourcePath));
            }

            return results;
        }

        private static void AddResourceParameters(
            SqlCommand command,
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            short embeddingModelId)
        {
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
            command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = searchParamId;
            command.Parameters.Add("@EmbeddingModelId", SqlDbType.SmallInt).Value = embeddingModelId;
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
    }
}
