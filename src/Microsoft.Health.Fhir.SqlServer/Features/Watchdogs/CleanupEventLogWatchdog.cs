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

            await LogRawResourceStats(cancellationToken);
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
    AND object_id NOT IN (object_id('Resource'), object_id('CompartmentAssignment'))
    AND index_id = 1
            ");
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, reader => reader.GetString(0), _logger, cancellationToken);
        }

        private async Task LogRawResourceStats(CancellationToken cancellationToken)
        {
            try
            {
                var st = DateTime.UtcNow;

                await CreateTmpProceduresAsync(cancellationToken);

                var maxSurrogateId = await GetMaxSurrogateId(cancellationToken);
                _logger.LogInformation($"DatabaseStats.MaxSurrogateId={maxSurrogateId}");

                var types = await GetUsedResourceTypesAsync(cancellationToken);
                _logger.LogInformation($"DatabaseStats.UsedResourceTypes.Count={types.Count}");

                foreach (var type in types)
                {
                    var totalCompressedBytes = 0L;
                    var totalDecompressedBytes = 0L;
                    var totalResources = 0L;
                    var surrogateId = 0L;
                    IReadOnlyList<(long SurrogateId, byte[] RawResourceBytes)> rawResources;
                    do
                    {
                        rawResources = await GetRawResourcesAsync(type, surrogateId, maxSurrogateId, cancellationToken);
                        foreach (var rawResource in rawResources)
                        {
                            surrogateId = rawResource.SurrogateId;
                            var rawResourceBytes = rawResource.RawResourceBytes;
                            var isInvisible = rawResourceBytes.Length == 1 && rawResourceBytes[0] == 0xF;
                            if (!isInvisible)
                            {
                                totalCompressedBytes += rawResourceBytes.Length;
                                using var rawResourceStream = new MemoryStream(rawResourceBytes);
                                totalDecompressedBytes += UTF8Encoding.UTF8.GetBytes(_compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream)).Length;
                                totalResources++;
                            }
                        }
                    }
                    while (rawResources.Count > 0);

                    var totals = new ResourceTypeTotals { ResourceTypeId = type, Resources = totalResources, CompressedBytes = totalCompressedBytes, DecompressedBytes = totalDecompressedBytes };
                    var totalsStr = JsonSerializer.Serialize(totals);
                    _logger.LogInformation($"DatabaseStats.ResourceTypeTotals={totalsStr}");
                    await _sqlRetryService.TryLogEvent("DatabaseStats.ResourceTypeTotals", "Warn", totalsStr, st, cancellationToken);
                }

                await DropTmpProceduresAsync(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "LogRawResourceStats failed.");
            }
        }

        private async Task<long> GetMaxSurrogateId(CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.tmp_GetMaxSurrogateId") { CommandType = CommandType.StoredProcedure };
            var maxSurrogateId = cmd.Parameters.Add("@MaxSurrogateId", SqlDbType.BigInt);
            maxSurrogateId.Direction = ParameterDirection.Output;
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (long)maxSurrogateId.Value;
        }

        private async Task<IReadOnlyList<short>> GetUsedResourceTypesAsync(CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("dbo.GetUsedResourceTypes") { CommandType = CommandType.StoredProcedure };
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, reader => reader.GetInt16(0), _logger, cancellationToken);
        }

        private async Task<IReadOnlyList<(long SurrogateId, byte[] RawResourceBytes)>> GetRawResourcesAsync(short resourceTypeId, long surrogateId, long maxSurrogateId, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("dbo.tmp_GetRawResources") { CommandType = CommandType.StoredProcedure };
            sqlCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            sqlCommand.Parameters.AddWithValue("@SurrogateId", surrogateId);
            sqlCommand.Parameters.AddWithValue("@MaxSurrogateId", maxSurrogateId);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, reader => (reader.GetInt64(0), reader.GetSqlBytes(1).Value), _logger, cancellationToken);
        }

        private async Task CreateTmpProceduresAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand(@"
CREATE OR ALTER PROCEDURE dbo.tmp_GetMaxSurrogateId @MaxSurrogateId bigint = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@ResourceTypeId smallint
       ,@MaxSurrogateIdTmp bigint
       ,@st datetime = getUTCdate()

SET @MaxSurrogateId = 0

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))

INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
WHILE EXISTS (SELECT * FROM @Types)
BEGIN
  SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types)
  SET @MaxSurrogateIdTmp = (SELECT max(ResourceSurrogateId) FROM Resource WHERE ResourceTypeId = @ResourceTypeId)
  IF @MaxSurrogateIdTmp > @MaxSurrogateId SET @MaxSurrogateId = @MaxSurrogateIdTmp
  DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId
END

EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Target='@MaxSurrogateId',@Action='Select',@Text=@MaxSurrogateId,@Start=@st
            ");
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            using var cmd2 = new SqlCommand("INSERT INTO dbo.Parameters (Id, Char) SELECT 'tmp_GetMaxSurrogateId', 'LogEvent'");
            await cmd2.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            using var cmd3 = new SqlCommand(@"
CREATE OR ALTER PROCEDURE dbo.tmp_GetRawResources @ResourceTypeId smallint, @SurrogateId bigint, @MaxSurrogateId bigint
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'RC='+convert(varchar,@ResourceTypeId)+' S='+convert(varchar,@SurrogateId)+' M='+convert(varchar,@MaxSurrogateId)
       ,@st datetime = getUTCdate()

SELECT TOP 10000
       ResourceSurrogateId, RawResource
  FROM dbo.Resource
  WHERE ResourceTypeId = @ResourceTypeId
    AND ResourceSurrogateId > @SurrogateId
    AND ResourceSurrogateId <= @MaxSurrogateId
  ORDER BY
       ResourceSurrogateId

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Target='Resource',@Action='Select',@Rows=@@rowcount,@Start=@st
            ");
            await cmd3.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            using var cmd4 = new SqlCommand("INSERT INTO dbo.Parameters (Id, Char) SELECT 'tmp_GetRawResources', 'LogEvent'");
            await cmd4.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            _logger.LogInformation("CreateTmpProceduresAsync completed.");
        }

        private async Task DropTmpProceduresAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand("DROP PROCEDURE dbo.tmp_GetMaxSurrogateId");
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            using var cmd2 = new SqlCommand("DROP PROCEDURE dbo.tmp_GetRawResources");
            await cmd2.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            _logger.LogInformation("DropTmpProceduresAsync completed.");
        }

        private class SearchParamCount
        {
            public string SearchParamTable { get; set; }

            public short SearchParamId { get; set; }

            public long RowCount { get; set; }
        }

        private class ResourceTypeTotals
        {
            public short ResourceTypeId { get; set; }

            public long Resources { get; set; }

            public long CompressedBytes { get; set; }

            public long DecompressedBytes { get; set; }
        }
    }
}
