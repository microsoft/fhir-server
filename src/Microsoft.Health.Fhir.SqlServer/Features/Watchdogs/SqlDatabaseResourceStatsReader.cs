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
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class SqlDatabaseResourceStatsReader : ISqlDatabaseResourceStatsReader
    {
        private const string Query = @"
SELECT TOP (1)
    end_time,
    avg_cpu_percent,
    avg_data_io_percent,
    avg_log_write_percent,
    avg_memory_usage_percent,
    max_worker_percent,
    max_session_percent,
    avg_instance_cpu_percent,
    avg_instance_memory_percent
FROM sys.dm_db_resource_stats
ORDER BY end_time DESC;";

        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlDatabaseResourceStatsReader> _logger;

        public SqlDatabaseResourceStatsReader(
            ISqlRetryService sqlRetryService,
            ILogger<SqlDatabaseResourceStatsReader> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<SqlDatabaseResourceStats> GetLatestAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(Query)
            {
                CommandType = CommandType.Text,
            };

            var results = await command.ExecuteReaderAsync(
                _sqlRetryService,
                Map,
                _logger,
                cancellationToken,
                "Failed to read SQL database resource metrics.");

            return results.Count == 0 ? null : results[0];
        }

        internal static SqlDatabaseResourceStats Map(System.Data.IDataRecord record)
        {
            EnsureArg.IsNotNull(record, nameof(record));

            var endTime = record.GetDateTime(0);
            var utcEndTime = endTime.Kind == DateTimeKind.Utc ? endTime : DateTime.SpecifyKind(endTime, DateTimeKind.Utc);

            return new SqlDatabaseResourceStats
            {
                EndTime = new DateTimeOffset(utcEndTime),
                CpuPercent = record.GetDouble(1),
                DataIoPercent = record.GetDouble(2),
                LogIoPercent = record.GetDouble(3),
                MemoryPercent = record.GetDouble(4),
                WorkersPercent = record.GetDouble(5),
                SessionsPercent = record.GetDouble(6),
                InstanceCpuPercent = record.IsDBNull(7) ? null : record.GetDouble(7),
                InstanceMemoryPercent = record.IsDBNull(8) ? null : record.GetDouble(8),
            };
        }
    }
}
