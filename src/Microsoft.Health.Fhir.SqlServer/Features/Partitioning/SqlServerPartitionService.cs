// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Partitioning;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitioning
{
    /// <summary>
    /// SQL Server implementation of the partition service.
    /// </summary>
    public class SqlServerPartitionService : IPartitionService
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly DataPartitioningConfiguration _partitioningConfig;
        private readonly SchemaInformation _schemaInformation;
        private readonly ILogger<SqlServerPartitionService> _logger;

        // Cache partition name to ID mappings for performance
        private readonly ConcurrentDictionary<string, int> _partitionCache = new();

        public SqlServerPartitionService(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IOptions<DataPartitioningConfiguration> partitioningConfig,
            SchemaInformation schemaInformation,
            ILogger<SqlServerPartitionService> logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _partitioningConfig = EnsureArg.IsNotNull(partitioningConfig?.Value, nameof(partitioningConfig));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public bool IsPartitioningEnabled()
        {
            // Feature flag must be enabled AND schema must support partitioning
            return _partitioningConfig.Enabled && _schemaInformation.Current >= SchemaVersionConstants.DataPartitioning;
        }

        public async Task<int> GetPartitionIdAsync(string partitionName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(partitionName, nameof(partitionName));

            // Check cache first
            if (_partitionCache.TryGetValue(partitionName, out int cachedId))
            {
                return cachedId;
            }

            using var connection = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using var command = connection.CreateRetrySqlCommand();

            command.CommandText = @"
                SELECT PartitionId
                FROM dbo.Partition
                WHERE PartitionName = @partitionName AND IsActive = 1";

            command.Parameters.Add(new SqlParameter("@partitionName", SqlDbType.VarChar, 64) { Value = partitionName });

            var result = await command.ExecuteScalarAsync(CancellationToken.None);

            if (result == null)
            {
                throw new ResourceNotFoundException($"Partition '{partitionName}' not found.");
            }

            int partitionId = (int)(short)result; // Cast from smallint to int

            // Cache the result
            _partitionCache.TryAdd(partitionName, partitionId);

            _logger.LogDebug("Retrieved partition ID {PartitionId} for partition {PartitionName}", partitionId, partitionName);

            return partitionId;
        }

        public async Task<int> CreatePartitionAsync(string partitionName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(partitionName, nameof(partitionName));

            if (!IsPartitioningEnabled())
            {
                throw new InvalidOperationException("Data partitioning is not enabled.");
            }

            _logger.LogInformation("Creating partition: {PartitionName}", partitionName);

            using var connection = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using var command = connection.CreateRetrySqlCommand();

            command.CommandText = @"
                INSERT INTO dbo.Partition (PartitionName, CreatedDate, IsActive)
                OUTPUT INSERTED.PartitionId
                VALUES (@partitionName, SYSDATETIMEOFFSET(), 1)";

            command.Parameters.Add(new SqlParameter("@partitionName", SqlDbType.VarChar, 64) { Value = partitionName });

            try
            {
                var result = await command.ExecuteScalarAsync(CancellationToken.None);
                int partitionId = (int)(short)result; // Cast from smallint to int

                // Cache the new partition
                _partitionCache.TryAdd(partitionName, partitionId);

                _logger.LogInformation("Created partition {PartitionName} with ID {PartitionId}", partitionName, partitionId);

                return partitionId;
            }
            catch (SqlException ex) when (ex.Number == 2627) // Unique constraint violation
            {
                // Partition already exists - try to get it
                _logger.LogWarning("Partition {PartitionName} already exists, retrieving existing ID", partitionName);
                return await GetPartitionIdAsync(partitionName);
            }
        }

        public string GetDefaultPartitionName()
        {
            return _partitioningConfig.DefaultPartitionName ?? "default";
        }

        public int GetSystemPartitionId()
        {
            return 1; // System partition is always ID 1
        }
    }
}
