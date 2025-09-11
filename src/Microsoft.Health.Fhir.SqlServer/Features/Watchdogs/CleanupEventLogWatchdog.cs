// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class CleanupEventLogWatchdog : Watchdog<CleanupEventLogWatchdog>
    {
        private readonly CompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<CleanupEventLogWatchdog> _logger;

        public CleanupEventLogWatchdog(ISqlRetryService sqlRetryService, ILogger<CleanupEventLogWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _compressedRawResourceConverter = new CompressedRawResourceConverter();
        }

        internal CleanupEventLogWatchdog()
        {
            // this is used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 3600;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 12 * 3600;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.CleanupEventLog") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            await LogSearchParamStats(cancellationToken);
        }

        protected override async Task InitAdditionalParamsAsync()
        {
            _logger.LogInformation("InitParamsAsync starting...");

            using var cmd = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.DeleteBatchSize', 1000
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.AllowedRows', 1e6
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.RetentionPeriodDay', 30
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.IsEnabled', 1
INSERT INTO dbo.Parameters (Id,Char) SELECT 'CleanpEventLog', 'LogEvent'
            ");
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None, "InitParamsAsync failed.");

            _logger.LogInformation("InitParamsAsync completed.");
        }

        // TODO: This is temporary code to get some stats (including raw resource length). We should determine what pieces are needed later and find permanent home for them.
        private async Task LogSearchParamStats(CancellationToken cancellationToken)
        {
            try
            {
                var searchParamTables = await GetSearchParamTables(cancellationToken);
                foreach (var searchParamTable in searchParamTables)
                {
                    var st = DateTime.UtcNow;
#pragma warning disable CA2100
                    using var sqlCommand = new SqlCommand($"SELECT SearchParamId, count_big(*) FROM dbo.{searchParamTable} GROUP BY SearchParamId") { CommandTimeout = 0 };
#pragma warning disable CA2100
                    var searchParamCounts = await sqlCommand.ExecuteReaderAsync(_sqlRetryService, reader => { return new SearchParamCount { SearchParamTable = searchParamTable, SearchParamId = reader.GetInt16(0), RowCount = reader.GetInt64(1) }; }, _logger, cancellationToken);
                    foreach (var searchParamCount in searchParamCounts)
                    {
                        var countStr = JsonSerializer.Serialize(searchParamCount);
                        _logger.LogInformation($"DatabaseStats.SearchParamCount={countStr}");
                        await _sqlRetryService.TryLogEvent("DatabaseStats.SearchParamCount", "Warn", countStr, st, cancellationToken);
                    }
                }
            }
            catch (SqlException e)
            {
                _logger.LogWarning(e, "LogSearchParamStats failed.");
            }
        }

        private async Task<IReadOnlyList<string>> GetSearchParamTables(CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand(@$"
SELECT object_name = object_name(object_id)
  FROM sys.indexes I
  WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND PS.name = 'PartitionScheme_ResourceTypeId')
    AND EXISTS (SELECT * FROM sys.columns C WHERE C.object_id = I.object_id AND name = 'SearchParamId')
    AND index_id = 1
            ");
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, reader => reader.GetString(0), _logger, cancellationToken);
        }

        private class SearchParamCount
        {
            public string SearchParamTable { get; set; }

            public short SearchParamId { get; set; }

            public long RowCount { get; set; }
        }
    }
}
