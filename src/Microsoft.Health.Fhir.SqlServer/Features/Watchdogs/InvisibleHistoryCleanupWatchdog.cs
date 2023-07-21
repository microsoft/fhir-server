// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class InvisibleHistoryCleanupWatchdog : Watchdog<InvisibleHistoryCleanupWatchdog>
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly ILogger<InvisibleHistoryCleanupWatchdog> _logger;
        private readonly ISqlRetryService _sqlRetryService;
        private CancellationToken _cancellationToken;
        private double _retentionPeriodDays = 7;

        public InvisibleHistoryCleanupWatchdog(SqlServerFhirDataStore store, ISqlRetryService sqlRetryService, ILogger<InvisibleHistoryCleanupWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal string LastCleanedUpTransactionId => $"{Name}.LastCleanedUpTransactionId";

        internal async Task StartAsync(CancellationToken cancellationToken, double? periodSec = null, double? leasePeriodSec = null, double? retentionPeriodDays = null)
        {
            _cancellationToken = cancellationToken;
            await InitLastCleanedUpTransactionId();
            await StartAsync(true, periodSec ?? 3600, leasePeriodSec ?? 2 * 3600, cancellationToken);
            if (retentionPeriodDays.HasValue)
            {
                _retentionPeriodDays = retentionPeriodDays.Value;
            }
        }

        protected override async Task ExecuteAsync()
        {
            _logger.LogInformation($"{Name}: starting...");
            var lastTranId = await GetLastCleanedUpTransactionId();
            var visibility = await _store.StoreClient.MergeResourcesGetTransactionVisibilityAsync(_cancellationToken);
            _logger.LogInformation($"{Name}: last cleaned up transaction={lastTranId} visibility={visibility}.");

            var transToClean = await _store.StoreClient.GetTransactionsAsync(lastTranId, visibility, _cancellationToken, DateTime.UtcNow.AddDays((-1) * _retentionPeriodDays));
            _logger.LogInformation($"{Name}: found transactions={transToClean.Count}.");

            if (transToClean.Count == 0)
            {
                _logger.LogInformation($"{Name}: completed. transactions=0.");
                return;
            }

            var totalRows = 0;
            foreach (var tran in transToClean.Where(_ => !_.InvisibleHistoryRemovedDate.HasValue).OrderBy(_ => _.TransactionId))
            {
                var rows = await _store.StoreClient.MergeResourcesDeleteInvisibleHistory(tran.TransactionId, _cancellationToken);
                _logger.LogInformation($"{Name}: transaction={tran.TransactionId} removed rows={rows}.");
                totalRows += rows;

                await _store.StoreClient.MergeResourcesPutTransactionInvisibleHistoryAsync(tran.TransactionId, _cancellationToken);
            }

            await UpdateLastCleanedUpTransactionId(transToClean.Max(_ => _.TransactionId));

            _logger.LogInformation($"{Name}: completed. transactions={transToClean.Count} removed rows={totalRows}");
        }

        private async Task<long> GetLastCleanedUpTransactionId()
        {
            return await GetLongParameterByIdAsync(LastCleanedUpTransactionId, _cancellationToken);
        }

        private async Task InitLastCleanedUpTransactionId()
        {
            using var cmd = new SqlCommand("INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, 5105975696064002770"); // surrogate id for the past. does not matter.
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }

        private async Task UpdateLastCleanedUpTransactionId(long lastTranId)
        {
            using var cmd = new SqlCommand("UPDATE dbo.Parameters SET Bigint = @LastTranId WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            cmd.Parameters.AddWithValue("@LastTranId", lastTranId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }
    }
}
