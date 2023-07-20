// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class SqlService
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlServerFhirDataStore> _logger;

        public SqlService(
            ISqlRetryService sqlRetryService,
            ILogger<SqlServerFhirDataStore> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        private async Task MergeResourcesPutTransactionHeartbeatAsync(long transactionId, TimeSpan heartbeatPeriod, CancellationToken cancellationToken)
        {
            try
            {
                using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionHeartbeat", CommandType = CommandType.StoredProcedure, CommandTimeout = (heartbeatPeriod.Seconds / 3) + 1 }; // +1 to avoid = SQL default timeout value
                cmd.Parameters.AddWithValue("@TransactionId", transactionId);
                await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
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
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
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

            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            return ((long)transactionIdParam.Value, (int)sequenceParam.Value);
        }

        internal async Task<int> MergeResourcesDeleteInvisibleHistory(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesDeleteInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            var affectedRowsParam = new SqlParameter("@affectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
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

            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
        }

        internal async Task MergeResourcesPutTransactionInvisibleHistoryAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
        }

        internal async Task<int> MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesAdvanceTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            var affectedRows = (int)affectedRowsParam.Value;
            return affectedRows;
        }

        internal async Task<IReadOnlyList<long>> MergeResourcesGetTimeoutTransactionsAsync(int timeoutSec, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTimeoutTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TimeoutSec", timeoutSec);
            return await _sqlRetryService.ExecuteSqlDataReader(cmd, (reader) => { return reader.GetInt64(0); }, _logger, null, cancellationToken);
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

            return await _sqlRetryService.ExecuteSqlDataReader(
                cmd,
                (reader) =>
                {
                    return (reader.Read(VLatest.Transactions.SurrogateIdRangeFirstValue, 0),
                            reader.Read(VLatest.Transactions.VisibleDate, 1),
                            reader.Read(VLatest.Transactions.InvisibleHistoryRemovedDate, 2));
                },
                _logger,
                null,
                cancellationToken);
        }

        internal async Task<IReadOnlyList<ResourceDateKey>> GetResourceDateKeysByTransactionIdAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@IncludeHistory", true);
            cmd.Parameters.AddWithValue("@ReturnResourceKeysOnly", true);
            return await _sqlRetryService.ExecuteSqlDataReader(cmd, ReadResourceDateKeyWrapper, _logger, null, cancellationToken);
        }
    }
}
