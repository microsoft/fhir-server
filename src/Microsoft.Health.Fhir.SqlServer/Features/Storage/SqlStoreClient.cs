// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
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
    internal class SqlStoreClient
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger _logger;
        internal const string InvisibleResource = " ";
        private readonly IRawResourceStore _blobRawResourceStore;

        public SqlStoreClient(ISqlRetryService sqlRetryService, ILogger<SqlStoreClient> logger, IRawResourceStore blobRawResourceStore = null)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _blobRawResourceStore = blobRawResourceStore;
        }

        public async Task HardDeleteAsync(short resourceTypeId, string resourceId, bool keepCurrentVersion, bool makeResourceInvisible, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.HardDeleteResource", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@ResourceId", resourceId);
            cmd.Parameters.AddWithValue("@KeepCurrentVersion", keepCurrentVersion);
            cmd.Parameters.AddWithValue("@MakeResourceInvisible", makeResourceInvisible);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await _sqlRetryService.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, Func<string, short> getResourceTypeId, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, bool isReadOnly, CancellationToken cancellationToken, bool includeInvisible = false)
        {
            return await GetAsync(keys.Select(_ => new ResourceDateKey(getResourceTypeId(_.ResourceType), _.Id, 0, _.VersionId)).ToList(), decompress, getResourceTypeName, isReadOnly, cancellationToken, includeInvisible);
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceDateKey> keys, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, bool isReadOnly, CancellationToken cancellationToken, bool includeInvisible = false)
        {
            if (keys == null || keys.Count == 0)
            {
                return new List<ResourceWrapper>();
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResources", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(2400.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceKeyListRow(_.ResourceTypeId, _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue));
            new ResourceKeyListTableValuedParameterDefinition("@ResourceKeys").AddParameter(cmd.Parameters, tvpRows);
            var start = DateTime.UtcNow;
            var timeoutRetries = 0;
            while (true)
            {
                try
                {
                    return await ReadResourceWrappersAsync(cmd, decompress, getResourceTypeName, isReadOnly, false, cancellationToken, includeInvisible);
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

                    throw;
                }
            }
        }

        private async Task<IReadOnlyList<ResourceWrapper>> ReadResourceWrappersAsync(SqlCommand cmd, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, bool isReadOnly, bool readRequestMethod, CancellationToken cancellationToken, bool includeInvisible = false)
        {
            var wrappers = (await cmd.ExecuteReaderAsync(_sqlRetryService, (reader) => { return ReadTemporaryResourceWrapper(reader, readRequestMethod, getResourceTypeName); }, _logger, cancellationToken, isReadOnly: isReadOnly)).ToList();
            var rawResources = await GetRawResourcesFromAdls(wrappers.Where(_ => _.SqlBytes.IsNull).Select(_ => new RawResourceLocator(_.FileId.Value, _.OffsetInFile.Value, _.ResourceLength.Value)).ToList());

            foreach (var wrapper in wrappers)
            {
                if (!wrapper.SqlBytes.IsNull)
                {
                    wrapper.Wrapper.RawResource = new RawResource(ReadCompressedRawResource(wrapper.SqlBytes, decompress), FhirResourceFormat.Json, wrapper.IsMetaSet);
                }
                else
                {
                    var key = new RawResourceLocator(wrapper.FileId.Value, wrapper.OffsetInFile.Value, wrapper.ResourceLength.Value);
                    wrapper.Wrapper.RawResource = new RawResource(rawResources[key], FhirResourceFormat.Json, wrapper.IsMetaSet);
                    wrapper.Wrapper.RawResourceLocator = key;
                }
            }

            return wrappers.Where(_ => includeInvisible || _.Wrapper.RawResource.Data != InvisibleResource).Select(_ => _.Wrapper).ToList();
        }

        internal async Task<IDictionary<RawResourceLocator, string>> GetRawResourcesFromAdls(IReadOnlyList<RawResourceLocator> resourceRefs)
        {
            var results = new Dictionary<RawResourceLocator, string>();
            if (resourceRefs == null || resourceRefs.Count == 0)
            {
                return results;
            }

            return await _blobRawResourceStore.ReadRawResourcesAsync(resourceRefs, CancellationToken.None);
        }

        internal static string ReadCompressedRawResource(SqlBytes bytes, Func<MemoryStream, string> decompress)
        {
            var rawResourceBytes = bytes.Value;
            string rawResource;
            if (rawResourceBytes.Length == 1 && rawResourceBytes[0] == 0xF) // invisible resource
            {
                rawResource = InvisibleResource;
            }
            else
            {
                using var rawResourceStream = new MemoryStream(rawResourceBytes);
                rawResource = decompress(rawResourceStream);
            }

            return rawResource;
        }

        public async Task<IReadOnlyList<(ResourceDateKey Key, (string Version, RawResource RawResource) Matched)>> GetResourceVersionsAsync(IReadOnlyList<ResourceDateKey> keys, Func<MemoryStream, string> decompress, CancellationToken cancellationToken)
        {
            if (keys == null || keys.Count == 0)
            {
                return new List<(ResourceDateKey Key, (string Version, RawResource RawResource) Matched)>();
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceVersions", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(1200.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceDateKeyListRow(_.ResourceTypeId, _.Id, _.ResourceSurrogateId));
            new ResourceDateKeyListTableValuedParameterDefinition("@ResourceDateKeys").AddParameter(cmd.Parameters, tvpRows);
            var table = VLatest.Resource;
            var tmpResources = await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                    var resourceId = reader.Read(table.ResourceId, 1);
                    var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                    var version = reader.Read(table.Version, 3);
                    string matchedVersion = null;
                    SqlBytes matchedBytes = null;
                    long? matchedFileId = null;
                    int? matchedOffsetInFile = null;
                    int? matchedResourceLength = null;
                    if (version == 0) // there is a match
                    {
                        matchedVersion = reader.Read(table.Version, 4).ToString();
                        matchedBytes = reader.GetSqlBytes(5);
                        matchedFileId = reader.FieldCount > 6 ? reader.Read(table.FileId, 6) : null; // TODO: Remove field count check after deployment
                        matchedOffsetInFile = reader.FieldCount > 6 ? reader.Read(table.OffsetInFile, 7) : null;
                        matchedResourceLength = reader.FieldCount > 6 ? reader.Read(table.ResourceLength, 8) : null; // TODO: Remove field count check after deployment
                    }

                    (ResourceDateKey DateKey, (string Version, SqlBytes Bytes, long? FileId, int? OffsetInFile, int? resourceLength) Matched) result;
                    result.DateKey = new ResourceDateKey(resourceTypeId, resourceId, resourceSurrogateId, version.ToString(CultureInfo.InvariantCulture));
                    result.Matched = (matchedVersion, matchedBytes, matchedFileId, matchedOffsetInFile, matchedResourceLength);
                    return result;
                },
                _logger,
                cancellationToken);

            var refs = tmpResources.Where(_ => _.Matched.Version != null && _.Matched.Bytes.IsNull).Select(_ => new RawResourceLocator(_.Matched.FileId.Value, _.Matched.OffsetInFile.Value, _.Matched.resourceLength.Value)).ToList();
            var rawResources = await GetRawResourcesFromAdls(refs);

            var resources = tmpResources.Select(_ =>
            {
                RawResource rawResource = null;
                if (_.Matched.Version != null)
                {
                    var key = new RawResourceLocator(EnsureArg.IsNotNull(_.Matched.FileId).Value, EnsureArg.IsNotNull(_.Matched.OffsetInFile).Value, EnsureArg.IsNotNull(_.Matched.resourceLength).Value);
                    rawResource = new RawResource(_.Matched.Bytes.IsNull ? rawResources[key] : ReadCompressedRawResource(_.Matched.Bytes, decompress), FhirResourceFormat.Json, false);
                }

                return (_.DateKey, (_.Matched.Version, rawResource));
            }).ToList();
            return resources;
        }

        internal async Task<IReadOnlyList<ResourceWrapper>> GetResourcesByTransactionIdAsync(long transactionId, Func<MemoryStream, string> decompress, Func<short, string> getResourceTypeName, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            return await ReadResourceWrappersAsync(cmd, decompress, getResourceTypeName, false, true, cancellationToken, false);
        }

        private static (ResourceWrapper Wrapper, bool IsMetaSet, SqlBytes SqlBytes, long? FileId, int? OffsetInFile, int? ResourceLength) ReadTemporaryResourceWrapper(SqlDataReader reader, bool readRequestMethod, Func<short, string> getResourceTypeName)
        {
            var resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            var resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            var resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 2);
            var version = reader.Read(VLatest.Resource.Version, 3);
            var isDeleted = reader.Read(VLatest.Resource.IsDeleted, 4);
            var isHistory = reader.Read(VLatest.Resource.IsHistory, 5);
            var bytes = reader.GetSqlBytes(6);
            var fileId = reader.FieldCount > 10 ? reader.Read(VLatest.Resource.FileId, readRequestMethod ? 10 : 9) : null; // TODO: Remove field count check after Lake schema deployment
            var offsetInFile = reader.FieldCount > 10 ? reader.Read(VLatest.Resource.OffsetInFile, readRequestMethod ? 11 : 10) : null;
            var resourceLength = reader.FieldCount > 11 ? reader.Read(VLatest.Resource.ResourceLength, readRequestMethod ? 12 : 11) : null; // TODO: Remove field count check after Lake schema deployment
            var isRawResourceMetaSet = reader.Read(VLatest.Resource.IsRawResourceMetaSet, 7);
            var searchParamHash = reader.Read(VLatest.Resource.SearchParamHash, 8);
            var requestMethod = readRequestMethod ? reader.Read(VLatest.Resource.RequestMethod, 9) : null;
            var wrapper = new ResourceWrapper(
                resourceId,
                version.ToString(CultureInfo.InvariantCulture),
                getResourceTypeName(resourceTypeId),
                null,
                readRequestMethod ? new ResourceRequest(requestMethod) : null,
                resourceSurrogateId.ToLastUpdated(),
                isDeleted,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: searchParamHash,
                resourceSurrogateId: resourceSurrogateId)
            {
                IsHistory = isHistory,
            };

            return (wrapper, isRawResourceMetaSet, bytes, fileId, offsetInFile, resourceLength);
        }

        internal async Task MergeResourcesPutTransactionHeartbeatAsync(long transactionId, TimeSpan heartbeatPeriod, CancellationToken cancellationToken)
        {
            try
            {
                await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionHeartbeat", CommandType = CommandType.StoredProcedure, CommandTimeout = (heartbeatPeriod.Seconds / 3) + 1 }; // +1 to avoid = SQL default timeout value
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
            await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (long)transactionIdParam.Value;
        }

        internal async Task<(long TransactionId, int Sequence)> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken, DateTime? heartbeatDate = null)
        {
            await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesBeginTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            var sequenceParam = new SqlParameter("@SequenceRangeFirstValue", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(sequenceParam);
            if (heartbeatDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@HeartbeatDate", heartbeatDate.Value);
            }

            // Code below has retries on execution timeouts.
            // Reason: GP databases are created with single data file. When database is heavily loaded by writes, single data file leads to long (up to several minutes) IO waits.
            // These waits cause intermittent execution timeouts even for very short (~10msec) calls.
            var start = DateTime.UtcNow;
            var timeoutRetries = 0;
            while (true)
            {
                try
                {
                    await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
                    return ((long)transactionIdParam.Value, (int)sequenceParam.Value);
                }
                catch (Exception e)
                {
                    if (e.IsExecutionTimeout() && timeoutRetries++ < 3)
                    {
                        _logger.LogWarning(e, $"Error on {nameof(MergeResourcesBeginTransactionAsync)} timeoutRetries={{TimeoutRetries}}", timeoutRetries);
                        await TryLogEvent(nameof(MergeResourcesBeginTransactionAsync), "Warn", $"timeout retries={timeoutRetries}", start, cancellationToken);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    throw;
                }
            }
        }

        internal async Task<int> MergeResourcesDeleteInvisibleHistory(long transactionId, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesDeleteInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            var affectedRowsParam = new SqlParameter("@affectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (int)affectedRowsParam.Value;
        }

        internal async Task MergeResourcesCommitTransactionAsync(long transactionId, string failureReason, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesCommitTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            if (failureReason != null)
            {
                cmd.Parameters.AddWithValue("@FailureReason", failureReason);
            }

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task MergeResourcesPutTransactionInvisibleHistoryAsync(long transactionId, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        internal async Task<int> MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand { CommandText = "dbo.MergeResourcesAdvanceTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            var affectedRows = (int)affectedRowsParam.Value;
            return affectedRows;
        }

        internal async Task<IReadOnlyList<long>> MergeResourcesGetTimeoutTransactionsAsync(int timeoutSec, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand { CommandText = "dbo.MergeResourcesGetTimeoutTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TimeoutSec", timeoutSec);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, reader => reader.GetInt64(0), _logger, cancellationToken);
        }

        internal async Task<IReadOnlyList<(long TransactionId, DateTime? VisibleDate, DateTime? InvisibleHistoryRemovedDate)>> GetTransactionsAsync(long startNotInclusiveTranId, long endInclusiveTranId, CancellationToken cancellationToken, DateTime? endDate = null)
        {
            await using var cmd = new SqlCommand { CommandText = "dbo.GetTransactions", CommandType = CommandType.StoredProcedure };
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
            await using var cmd = new SqlCommand { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@IncludeHistory", true);
            cmd.Parameters.AddWithValue("@ReturnResourceKeysOnly", true);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, ReadResourceDateKeyWrapper, _logger, cancellationToken);
        }
    }
}
