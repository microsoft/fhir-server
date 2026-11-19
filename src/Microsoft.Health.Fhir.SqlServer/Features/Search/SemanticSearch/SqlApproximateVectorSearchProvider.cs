// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// Detects a compatible DiskANN vector index and applies the approximate-search setting.
    /// </summary>
    /// <remarks>
    /// Approximate retrieval requires an index on the vector column that is a DiskANN index of the latest format
    /// (version 3 or later, which supports the <c>TOP (N) WITH APPROXIMATE</c> syntax and iterative filtering) and
    /// that uses the configured distance metric. An earlier-format index returns post-filtered results, so it is
    /// not used. A positive result is kept for the life of the process; the engine falls back to an exact scan by
    /// itself if the index is later dropped. A negative result is rechecked periodically so that an index created
    /// while the server is running is picked up without a restart.
    /// </remarks>
    internal sealed class SqlApproximateVectorSearchProvider : IApproximateVectorSearchProvider
    {
        internal static readonly TimeSpan UnavailableRecheckInterval = TimeSpan.FromMinutes(5);

        private const int MinimumIndexVersion = 3;

        private const string ProbeIndex = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM sys.vector_indexes AS vi
    JOIN sys.index_columns AS ic ON ic.object_id = vi.object_id AND ic.index_id = vi.index_id
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE vi.object_id = OBJECT_ID(@TableName)
      AND c.name = @ColumnName
      AND vi.vector_index_type = 'DiskANN'
      AND UPPER(vi.distance_metric) = UPPER(@DistanceMetric)
      AND vi.is_disabled = 0
      AND TRY_CONVERT(int, JSON_VALUE(vi.build_parameters, '$.Version')) >= @MinimumVersion
) THEN 1 ELSE 0 END";

        private readonly ISqlRetryService _sqlRetryService;
        private readonly VectorSearchConfiguration _configuration;
        private readonly ILogger<SqlApproximateVectorSearchProvider> _logger;
        private readonly TimeProvider _timeProvider;
        private ProbeResult _lastProbe;

        public SqlApproximateVectorSearchProvider(
            ISqlRetryService sqlRetryService,
            IOptions<VectorSearchConfiguration> configuration,
            ILogger<SqlApproximateVectorSearchProvider> logger,
            TimeProvider timeProvider = null)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _configuration = EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _timeProvider = timeProvider ?? ClockResolver.TimeProvider;
        }

        /// <inheritdoc />
        public async Task<int?> GetCandidateMultiplierAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.Enabled || _configuration.Query.ApproximateSearch != VectorSearchApproximateMode.Auto)
            {
                return null;
            }

            ProbeResult probe = Volatile.Read(ref _lastProbe);
            if (probe == null || (!probe.IsAvailable && _timeProvider.GetUtcNow() >= probe.RecheckAt))
            {
                probe = await ProbeAsync(probe, cancellationToken);
                Volatile.Write(ref _lastProbe, probe);
            }

            return probe.IsAvailable ? _configuration.Query.ApproximateCandidateMultiplier : null;
        }

        private async Task<ProbeResult> ProbeAsync(ProbeResult previous, CancellationToken cancellationToken)
        {
            bool isAvailable = false;
            try
            {
                using var command = new SqlCommand { CommandText = ProbeIndex };
                command.Parameters.Add("@TableName", SqlDbType.NVarChar, 256).Value = VLatest.VectorSearchParam.TableName;
                command.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 128).Value = VLatest.VectorSearchParam.Embedding.ToString();
                command.Parameters.Add("@DistanceMetric", SqlDbType.VarChar, 16).Value = _configuration.Query.DistanceMetric;
                command.Parameters.Add("@MinimumVersion", SqlDbType.Int).Value = MinimumIndexVersion;

                var results = await command.ExecuteReaderAsync(
                    _sqlRetryService,
                    reader => reader.GetInt32(0) == 1,
                    _logger,
                    cancellationToken,
                    "Detect approximate vector index",
                    isReadOnly: true);
                isAvailable = results.Count == 1 && results[0];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not determine whether an approximate vector index is available. Semantic queries rank exactly until the next check.");
                return new ProbeResult(false, _timeProvider.GetUtcNow() + UnavailableRecheckInterval);
            }

            if (isAvailable && previous?.IsAvailable != true)
            {
                _logger.LogInformation("Approximate vector search is enabled and a compatible DiskANN index was found. Relevance-ranked semantic queries use it.");
            }
            else if (!isAvailable && previous?.IsAvailable != false)
            {
                _logger.LogWarning(
                    "Approximate vector search is enabled, but no compatible vector index (DiskANN, version {MinimumVersion} or later, {DistanceMetric}) exists on {Table}.{Column}. Semantic queries rank exactly and the check repeats every {Minutes} minutes.",
                    MinimumIndexVersion,
                    _configuration.Query.DistanceMetric,
                    VLatest.VectorSearchParam.TableName,
                    VLatest.VectorSearchParam.Embedding,
                    UnavailableRecheckInterval.TotalMinutes);
            }

            return new ProbeResult(isAvailable, _timeProvider.GetUtcNow() + UnavailableRecheckInterval);
        }

        private sealed record ProbeResult(bool IsAvailable, DateTimeOffset RecheckAt);
    }
}
