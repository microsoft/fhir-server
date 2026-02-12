// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Stats;
using Microsoft.Health.Fhir.Core.Messages.Stats;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Stats
{
    /// <summary>
    /// Provides resource statistics from SQL Server.
    /// </summary>
    public class SqlServerStatsProvider : IStatsProvider
    {
        private readonly SqlConnectionWrapperFactory _connectionFactory;
        private readonly ILogger<SqlServerStatsProvider> _logger;

        public SqlServerStatsProvider(SqlConnectionWrapperFactory connectionFactory, ILogger<SqlServerStatsProvider> logger)
        {
            EnsureArg.IsNotNull(connectionFactory, nameof(connectionFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<StatsResponse> GetStatsAsync(CancellationToken cancellationToken)
        {
            var stats = new StatsResponse();

            using var conn = await _connectionFactory.ObtainSqlConnectionWrapperAsync(cancellationToken);
            using var cmd = new SqlCommand("dbo.GetResourceStats", conn.SqlConnection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var resourceType = reader.GetString(0);
                var totalCount = reader.GetInt64(1);
                var activeCount = reader.GetInt64(2);
                stats.ResourceStats[resourceType] = new ResourceTypeStats
                {
                    TotalCount = totalCount,
                    ActiveCount = activeCount,
                };
            }

            return stats;
        }
    }
}
