// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class SqlQueryStoreWatchdog : Watchdog<SqlQueryStoreWatchdog>
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlQueryStoreWatchdog> _logger;

        public SqlQueryStoreWatchdog(ISqlRetryService sqlRetryService, ILogger<SqlQueryStoreWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal SqlQueryStoreWatchdog()
        {
            // this is used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 3600;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 3600; // Run every hour by default

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            await LogQueryStoreStats(cancellationToken);
        }

        protected override async Task InitAdditionalParamsAsync()
        {
            _logger.LogInformation("SqlQueryStoreWatchdog.InitParamsAsync starting...");

            using var cmd = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id,Number) SELECT 'SqlQueryStore.IsEnabled', 1
INSERT INTO dbo.Parameters (Id,Number) SELECT 'SqlQueryStore.TopQueriesCount', 50
            ");
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None, "InitParamsAsync failed.");

            _logger.LogInformation("SqlQueryStoreWatchdog.InitParamsAsync completed.");
        }

        private async Task LogQueryStoreStats(CancellationToken cancellationToken)
        {
            try
            {
                var isEnabled = await GetNumberParameterByIdAsync("SqlQueryStore.IsEnabled", cancellationToken);
                if (isEnabled == 0)
                {
                    _logger.LogDebug("SqlQueryStoreWatchdog is disabled.");
                    return;
                }

                var topQueriesCount = (int)await GetNumberParameterByIdAsync("SqlQueryStore.TopQueriesCount", cancellationToken);

                // Get top queries from Query Store with their index usage
                var queryStats = await GetTopQueriesWithIndexUsage(topQueriesCount, cancellationToken);

                foreach (var stat in queryStats)
                {
                    var statJson = JsonSerializer.Serialize(stat);
                    _logger.LogInformation($"SqlQueryStore.QueryStats={statJson}");
                }

                _logger.LogInformation($"SqlQueryStoreWatchdog logged {queryStats.Count} query statistics.");
            }
            catch (SqlException e)
            {
                _logger.LogWarning(e, "LogQueryStoreStats failed.");
            }
        }

        private async Task<IReadOnlyList<QueryStoreStats>> GetTopQueriesWithIndexUsage(int topCount, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand($@"
-- Get top queries by total execution time with their index usage from Query Store
SELECT TOP (@TopCount)
    q.query_id,
    SUBSTRING(qt.query_sql_text, 1, 1000) AS query_sql_text,
    p.plan_id,
    CAST(rs.avg_duration / 1000.0 AS DECIMAL(18,2)) AS avg_duration_ms,
    CAST(rs.avg_cpu_time / 1000.0 AS DECIMAL(18,2)) AS avg_cpu_time_ms,
    CAST(rs.avg_logical_io_reads AS BIGINT) AS avg_logical_reads,
    CAST(rs.avg_physical_io_reads AS BIGINT) AS avg_physical_reads,
    rs.count_executions,
    CAST((rs.avg_duration * rs.count_executions) / 1000.0 AS DECIMAL(18,2)) AS total_duration_ms,
    STUFF((
        SELECT DISTINCT ', ' + 
            QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(OBJECT_NAME(i.object_id)) + '.' + QUOTENAME(i.name)
        FROM (
            SELECT 
                ref.value('(@Database)[1]', 'NVARCHAR(128)') AS database_name,
                ref.value('(@Schema)[1]', 'NVARCHAR(128)') AS schema_name,
                ref.value('(@Table)[1]', 'NVARCHAR(128)') AS table_name,
                ref.value('(@Index)[1]', 'NVARCHAR(128)') AS index_name
            FROM (
                SELECT TRY_CAST(p.query_plan AS XML) AS plan_xml
            ) AS plans
            CROSS APPLY plan_xml.nodes('//RelOp//IndexScan//Object') AS indexes(ref)
            WHERE ref.value('(@Index)[1]', 'NVARCHAR(128)') IS NOT NULL
            UNION
            SELECT 
                ref.value('(@Database)[1]', 'NVARCHAR(128)') AS database_name,
                ref.value('(@Schema)[1]', 'NVARCHAR(128)') AS schema_name,
                ref.value('(@Table)[1]', 'NVARCHAR(128)') AS table_name,
                ref.value('(@Index)[1]', 'NVARCHAR(128)') AS index_name
            FROM (
                SELECT TRY_CAST(p.query_plan AS XML) AS plan_xml
            ) AS plans
            CROSS APPLY plan_xml.nodes('//RelOp//IndexSeek//Object') AS indexes(ref)
            WHERE ref.value('(@Index)[1]', 'NVARCHAR(128)') IS NOT NULL
        ) AS idx_refs
        INNER JOIN sys.objects o ON o.name = idx_refs.table_name AND SCHEMA_NAME(o.schema_id) = idx_refs.schema_name
        INNER JOIN sys.indexes i ON i.object_id = o.object_id AND i.name = idx_refs.index_name
        FOR XML PATH('')
    ), 1, 2, '') AS indexes_used
FROM sys.query_store_query q
INNER JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
INNER JOIN sys.query_store_plan p ON q.query_id = p.query_id
INNER JOIN (
    SELECT 
        plan_id,
        SUM(count_executions) AS count_executions,
        SUM(avg_duration * count_executions) / NULLIF(SUM(count_executions), 0) AS avg_duration,
        SUM(avg_cpu_time * count_executions) / NULLIF(SUM(count_executions), 0) AS avg_cpu_time,
        SUM(avg_logical_io_reads * count_executions) / NULLIF(SUM(count_executions), 0) AS avg_logical_io_reads,
        SUM(avg_physical_io_reads * count_executions) / NULLIF(SUM(count_executions), 0) AS avg_physical_io_reads,
        MAX(last_execution_time) AS last_execution_time
    FROM sys.query_store_runtime_stats
    GROUP BY plan_id
) rs ON p.plan_id = rs.plan_id
WHERE rs.last_execution_time >= DATEADD(hour, -24, GETUTCDATE())
    AND qt.query_sql_text NOT LIKE '%sys.query_store%'
    AND rs.count_executions > 0
ORDER BY (rs.avg_duration * rs.count_executions) DESC
            ") { CommandTimeout = 300 };

            sqlCommand.Parameters.AddWithValue("@TopCount", topCount);

            return await sqlCommand.ExecuteReaderAsync(
                _sqlRetryService,
                reader =>
                {
                    return new QueryStoreStats
                    {
                        QueryId = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                        QueryText = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        PlanId = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        AvgDurationMs = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                        AvgCpuTimeMs = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                        AvgLogicalReads = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                        AvgPhysicalReads = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                        CountExecutions = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                        TotalDurationMs = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                        IndexesUsed = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    };
                },
                _logger,
                cancellationToken);
        }

        private class QueryStoreStats
        {
            public long QueryId { get; set; }

            public string QueryText { get; set; }

            public long PlanId { get; set; }

            public decimal AvgDurationMs { get; set; }

            public decimal AvgCpuTimeMs { get; set; }

            public long AvgLogicalReads { get; set; }

            public long AvgPhysicalReads { get; set; }

            public long CountExecutions { get; set; }

            public decimal TotalDurationMs { get; set; }

            public string IndexesUsed { get; set; }
        }
    }
}
