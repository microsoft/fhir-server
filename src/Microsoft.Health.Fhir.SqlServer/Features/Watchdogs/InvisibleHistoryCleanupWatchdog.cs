// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class InvisibleHistoryCleanupWatchdog : Watchdog<InvisibleHistoryCleanupWatchdog>
    {
        private readonly SqlStoreClient _store;
        private readonly ILogger<InvisibleHistoryCleanupWatchdog> _logger;
        private readonly ISqlRetryService _sqlRetryService;

        public InvisibleHistoryCleanupWatchdog(SqlStoreClient store, ISqlRetryService sqlRetryService, ILogger<InvisibleHistoryCleanupWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal InvisibleHistoryCleanupWatchdog()
        {
            // this is used to get param names for testing
        }

        public string LastCleanedUpTransactionId => $"{Name}.LastCleanedUpTransactionId";

        public override double LeasePeriodSec { get; internal set; } = 2 * 3600;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 3600;

        public string RetentionPeriodDaysId => $"{Name}.RetentionPeriodDays";

        public double RetentionPeriodDays { get; internal set; } = 7;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name}: starting...");
            var visibility = await _store.MergeResourcesGetTransactionVisibilityAsync(cancellationToken);
            _logger.LogInformation($"{Name}: visibility={visibility}.");
            var totalRows = 0;
            var totalTrans = 0;
            var transToCleanCount = 0;
            do
            {
                var lastTranId = await GetLastCleanedUpTransactionIdAsync(cancellationToken);
                _logger.LogInformation($"{Name}: last cleaned up transaction={lastTranId}.");
                var transToClean = await _store.GetTransactionsAsync(lastTranId, visibility, cancellationToken, DateTime.UtcNow.AddDays(-1 * RetentionPeriodDays));
                _logger.LogInformation($"{Name}: found transactions={transToClean.Count}.");
                if (transToClean.Count > 0)
                {
                    foreach (var tran in transToClean.Where(x => !x.InvisibleHistoryRemovedDate.HasValue).OrderBy(x => x.TransactionId))
                    {
                        var rows = await _store.MergeResourcesDeleteInvisibleHistory(tran.TransactionId, cancellationToken);
                        _logger.LogInformation($"{Name}: transaction={tran.TransactionId} removed rows={rows}.");
                        totalRows += rows;
                        totalTrans++;

                        await _store.MergeResourcesPutTransactionInvisibleHistoryAsync(tran.TransactionId, cancellationToken);
                    }

                    var cleanedUpToTranId = transToClean.Max(_ => _.TransactionId);
                    await UpdateLastCleanedUpTransactionId(cleanedUpToTranId);
                    _logger.LogInformation($"{Name}: cleaned up to {cleanedUpToTranId}. transactions={totalTrans} removed rows={totalRows}");
                }

                transToCleanCount = transToClean.Count;
            }
            while (transToCleanCount > 0);

            _logger.LogInformation($"{Name}: completed. transactions={totalTrans} removed rows={totalRows}");
        }

        private async Task<long> GetLastCleanedUpTransactionIdAsync(CancellationToken cancellationToken)
        {
            return await GetLongParameterByIdAsync(LastCleanedUpTransactionId, cancellationToken);
        }

        protected override async Task InitAdditionalParamsAsync()
        {
            await using var cmd = new SqlCommand("INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, 5105975696064002770"); // surrogate id for the past. does not matter.
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);

            await using var cmd2 = new SqlCommand("INSERT INTO dbo.Parameters (Id,Number) SELECT @RetentionPeriodDaysId, @RetentionPeriodDays");
            cmd2.Parameters.AddWithValue("@RetentionPeriodDaysId", @RetentionPeriodDaysId);
            cmd2.Parameters.AddWithValue("@RetentionPeriodDays", @RetentionPeriodDays);
            await cmd2.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
            RetentionPeriodDays = await GetNumberParameterByIdAsync(RetentionPeriodDaysId, CancellationToken.None);
        }

        private async Task UpdateLastCleanedUpTransactionId(long lastTranId)
        {
            await using var cmd = new SqlCommand(@"
UPDATE dbo.Parameters SET Bigint = @LastTranId WHERE Id = @Id
EXECUTE dbo.LogEvent @Process='UpdateLastCleanedUpTransactionId',@Status='Warn',@Rows=@@rowcount,@Text=@LastTranId
            ");
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            cmd.Parameters.AddWithValue("@LastTranId", lastTranId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }
    }
}
