// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves and caches the database identifier for the configured embedding model.
    /// </summary>
    public sealed class SqlEmbeddingModelRegistry : IEmbeddingModelRegistry, IDisposable
    {
        private const string ResolveEmbeddingModel = @"
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

DECLARE @EmbeddingModelId SMALLINT;

SELECT @EmbeddingModelId = EmbeddingModelId
FROM dbo.EmbeddingModel WITH (UPDLOCK, HOLDLOCK)
WHERE ModelName = @ModelName
  AND ModelVersion = @ModelVersion;

IF @EmbeddingModelId IS NULL
BEGIN
    INSERT INTO dbo.EmbeddingModel (ModelName, ModelVersion, Dimension, DistanceMetric)
    VALUES (@ModelName, @ModelVersion, @Dimension, @DistanceMetric);

    SET @EmbeddingModelId = CONVERT(SMALLINT, SCOPE_IDENTITY());
END
ELSE IF EXISTS
(
    SELECT 1
    FROM dbo.EmbeddingModel
    WHERE EmbeddingModelId = @EmbeddingModelId
      AND (Dimension <> @Dimension OR DistanceMetric <> @DistanceMetric)
)
BEGIN
    THROW 50000, 'The configured embedding model metadata does not match the existing registry row.', 1;
END;

COMMIT TRANSACTION;
SELECT @EmbeddingModelId;";

        private readonly string _connectionString;
        private readonly VectorSearchConfiguration _configuration;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private short? _embeddingModelId;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlEmbeddingModelRegistry"/> class.
        /// </summary>
        /// <param name="connectionString">The SQL database connection string.</param>
        /// <param name="configuration">The vector-search configuration.</param>
        public SqlEmbeddingModelRegistry(string connectionString, IOptions<VectorSearchConfiguration> configuration)
        {
            _connectionString = EnsureArg.IsNotNullOrWhiteSpace(connectionString, nameof(connectionString));
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
        }

        /// <inheritdoc />
        public async Task<short> GetEmbeddingModelIdAsync(CancellationToken cancellationToken)
        {
            if (_embeddingModelId.HasValue)
            {
                return _embeddingModelId.Value;
            }

            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (_embeddingModelId.HasValue)
                {
                    return _embeddingModelId.Value;
                }

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = ResolveEmbeddingModel;
                command.Parameters.Add("@ModelName", SqlDbType.VarChar, 128).Value = _configuration.Embedding.ModelName;
                command.Parameters.Add("@ModelVersion", SqlDbType.VarChar, 64).Value = _configuration.Embedding.ModelVersion;
                command.Parameters.Add("@Dimension", SqlDbType.Int).Value = _configuration.Embedding.Dimensions;
                command.Parameters.Add("@DistanceMetric", SqlDbType.VarChar, 16).Value = _configuration.Query.DistanceMetric;

                object result = await command.ExecuteScalarAsync(cancellationToken);
                _embeddingModelId = Convert.ToInt16(result, CultureInfo.InvariantCulture);
                return _embeddingModelId.Value;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _initializationLock.Dispose();
        }
    }
}
