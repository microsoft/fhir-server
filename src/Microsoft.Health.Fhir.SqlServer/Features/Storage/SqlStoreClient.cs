// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Lightweight SQL store client.
    /// </summary>
    /// <typeparam name="TLogger">Iloger</typeparam>
    internal class SqlStoreClient<TLogger>
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<TLogger> _logger;

        public SqlStoreClient(ISqlRetryService sqlRetryService, ILogger<TLogger> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task HardDeleteAsync(short resourceTypeId, string resourceId, bool keepCurrentVersion, bool isResourceChangeCaptureEnabled, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.HardDeleteResource", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@ResourceId", resourceId);
            cmd.Parameters.AddWithValue("@KeepCurrentVersion", keepCurrentVersion);
            cmd.Parameters.AddWithValue("@IsResourceChangeCaptureEnabled", isResourceChangeCaptureEnabled);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await _sqlRetryService.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, Func<string, short> getResourceTypeId, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, CancellationToken cancellationToken)
        {
            if (keys == null || keys.Count == 0)
            {
                return new List<ResourceWrapper>();
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResources", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(2400.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceKeyListRow(getResourceTypeId(_.ResourceType), _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue));
            new ResourceKeyListTableValuedParameterDefinition("@ResourceKeys").AddParameter(cmd.Parameters, tvpRows);
            var start = DateTime.UtcNow;
            var timeoutRetries = 0;
            while (true)
            {
                try
                {
                    return await cmd.ExecuteReaderAsync(_sqlRetryService, (reader) => { return ReadResourceWrapper(reader, false, decompress, getResourceTypeName); }, _logger, cancellationToken);
                }
                catch (Exception e)
                {
                    if (e.IsExecutionTimeout() && timeoutRetries++ < 3)
                    {
                        _logger.LogWarning(e, $"Error on {nameof(GetAsync)} timeoutRetries={{TimeoutRetries}}", timeoutRetries);
                        await TryLogEvent(nameof(GetAsync), "Warn", $"timeout retries={timeoutRetries}", start, cancellationToken);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }
                }
            }
        }

        public async Task<IReadOnlyList<ResourceDateKey>> GetResourceVersionsAsync(IReadOnlyList<ResourceDateKey> keys, CancellationToken cancellationToken)
        {
            if (keys == null || keys.Count == 0)
            {
                return new List<ResourceDateKey>();
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceVersions", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(1200.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceDateKeyListRow(_.ResourceTypeId, _.Id, _.ResourceSurrogateId));
            new ResourceDateKeyListTableValuedParameterDefinition("@ResourceDateKeys").AddParameter(cmd.Parameters, tvpRows);
            var table = VLatest.Resource;
            var resources = await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                    var resourceId = reader.Read(table.ResourceId, 1);
                    var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                    var version = reader.Read(table.Version, 3);
                    return new ResourceDateKey(resourceTypeId, resourceId, resourceSurrogateId, version.ToString(CultureInfo.InvariantCulture));
                },
                _logger,
                cancellationToken);
            return resources;
        }

        internal async Task<IReadOnlyList<ResourceWrapper>> GetResourcesByTransactionIdAsync(long transactionId, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, (reader) => { return ReadResourceWrapper(reader, true, decompress, getResourceTypeName); }, _logger, cancellationToken);
        }

        private static ResourceWrapper ReadResourceWrapper(SqlDataReader reader, bool readRequestMethod, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName)
        {
            var resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            var resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            var resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 2);
            var version = reader.Read(VLatest.Resource.Version, 3);
            var isDeleted = reader.Read(VLatest.Resource.IsDeleted, 4);
            var isHistory = reader.Read(VLatest.Resource.IsHistory, 5);
            var rawResourceBytes = reader.GetSqlBytes(6).Value;
            var isRawResourceMetaSet = reader.Read(VLatest.Resource.IsRawResourceMetaSet, 7);
            var searchParamHash = reader.Read(VLatest.Resource.SearchParamHash, 8);
            var requestMethod = readRequestMethod ? reader.Read(VLatest.Resource.RequestMethod, 9) : null;

            using var rawResourceStream = new MemoryStream(rawResourceBytes);
            var rawResource = decompress(rawResourceStream);

            return new ResourceWrapper(
                resourceId,
                version.ToString(CultureInfo.InvariantCulture),
                getResourceTypeName(resourceTypeId),
                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                readRequestMethod ? new ResourceRequest(requestMethod) : null,
                new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                isDeleted,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: searchParamHash,
                resourceSurrogateId: resourceSurrogateId)
            {
                IsHistory = isHistory,
            };
        }

        internal async Task MergeResourcesPutTransactionHeartbeatAsync(long transactionId, TimeSpan heartbeatPeriod, CancellationToken cancellationToken)
        {
            try
            {
                using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionHeartbeat", CommandType = CommandType.StoredProcedure, CommandTimeout = (heartbeatPeriod.Seconds / 3) + 1 }; // +1 to avoid = SQL default timeout value
                cmd.Parameters.AddWithValue("@TransactionId", transactionId);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeResourcesPutTransactionHeartbeatAsync)}");
            }
        }

        private ResourceDateKey ReadResourceDateKeyWrapper(SqlDataReader reader)
        {
            var resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            var resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            var resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 2);
            var version = reader.Read(VLatest.Resource.Version, 3);
            var isDeleted = reader.Read(VLatest.Resource.IsDeleted, 4);

            return new ResourceDateKey(resourceTypeId, resourceId, resourceSurrogateId, version.ToString(CultureInfo.InvariantCulture), isDeleted);
        }

        internal async Task<long> MergeResourcesGetTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (long)transactionIdParam.Value;
        }

        internal async Task<(long TransactionId, int Sequence)> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken, DateTime? heartbeatDate = null)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesBeginTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            var sequenceParam = new SqlParameter("@SequenceRangeFirstValue", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(sequenceParam);
            if (heartbeatDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@HeartbeatDate", heartbeatDate.Value);
            }

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return ((long)transactionIdParam.Value, (int)sequenceParam.Value);
        }

        internal async Task<int> MergeResourcesDeleteInvisibleHistory(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesDeleteInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            var affectedRowsParam = new SqlParameter("@affectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (int)affectedRowsParam.Value;
        }

        internal async Task MergeResourcesCommitTransactionAsync(long transactionId, string failureReason, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesCommitTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            if (failureReason != null)
            {
                cmd.Parameters.AddWithValue("@FailureReason", failureReason);
            }

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task MergeResourcesPutTransactionInvisibleHistoryAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task<int> MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesAdvanceTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            var affectedRows = (int)affectedRowsParam.Value;
            return affectedRows;
        }

        internal async Task<IReadOnlyList<long>> MergeResourcesGetTimeoutTransactionsAsync(int timeoutSec, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTimeoutTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TimeoutSec", timeoutSec);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, (reader) => { return reader.GetInt64(0); }, _logger, cancellationToken);
        }

        internal async Task<IReadOnlyList<(long TransactionId, DateTime? VisibleDate, DateTime? InvisibleHistoryRemovedDate)>> GetTransactionsAsync(long startNotInclusiveTranId, long endInclusiveTranId, CancellationToken cancellationToken, DateTime? endDate = null)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@StartNotInclusiveTranId", startNotInclusiveTranId);
            cmd.Parameters.AddWithValue("@EndInclusiveTranId", endInclusiveTranId);
            if (endDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@EndDate", endDate.Value);
            }

            return await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    return (reader.Read(VLatest.Transactions.SurrogateIdRangeFirstValue, 0),
                            reader.Read(VLatest.Transactions.VisibleDate, 1),
                            reader.Read(VLatest.Transactions.InvisibleHistoryRemovedDate, 2));
                },
                _logger,
                cancellationToken);
        }

        internal async Task<IReadOnlyList<ResourceDateKey>> GetResourceDateKeysByTransactionIdAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@IncludeHistory", true);
            cmd.Parameters.AddWithValue("@ReturnResourceKeysOnly", true);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, ReadResourceDateKeyWrapper, _logger, cancellationToken);
        }
    }
}
