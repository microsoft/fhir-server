// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class InvisibleHistoryCleanupWatchdog : Watchdog<InvisibleHistoryCleanupWatchdog>
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly ILogger<InvisibleHistoryCleanupWatchdog> _logger;
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private CancellationToken _cancellationToken;
        private const double _periodSec = 1 * 3600;
        private const double _leasePeriodSec = 2 * 3600;
        private const double _retentionPeriodDays = 30;

        public InvisibleHistoryCleanupWatchdog(SqlServerFhirDataStore store, Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory, ILogger<InvisibleHistoryCleanupWatchdog> logger)
            : base(sqlConnectionWrapperFactory, logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal string LastCleanedUpTransactionId => $"{Name}.LastCleanedUpTransactionId";

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await InitLastCleanedUpTransactionId();
            await StartAsync(true, _periodSec, _leasePeriodSec, cancellationToken);
        }

        protected override async Task ExecuteAsync()
        {
            _logger.LogInformation($"{Name}: starting...");
            var lastTranId = await GetLastCleanedUpTransactionId();
            var visibility = await _store.MergeResourcesGetTransactionVisibilityAsync(_cancellationToken);
            _logger.LogInformation($"{Name}: last cleaned up transaction={lastTranId} visibility={visibility}.");

            var transToClean = await _store.GetTransactionsAsync(lastTranId, visibility, _cancellationToken, DateTime.UtcNow.AddDays((-1) * _retentionPeriodDays));
            _logger.LogInformation($"{Name}: found transactions={transToClean.Count}.");

            if (transToClean.Count == 0)
            {
                _logger.LogInformation($"{Name}: completed. transactions=0.");
                return;
            }

            var totalRows = 0;
            foreach (var tran in transToClean.Where(_ => !_.InvisibleHistoryRemovedDate.HasValue).OrderBy(_ => _.TransactionId))
            {
                var rows = await _store.MergeResourcesDeleteInvisibleHistory(tran.TransactionId, _cancellationToken);
                _logger.LogInformation($"{Name}: transaction={tran.TransactionId} removed rows={rows}.");
                totalRows += rows;

                await _store.MergeResourcesPutTransactionInvisibleHistoryAsync(tran.TransactionId, _cancellationToken);
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
            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, 5105975696064002770"; // surrogate id for the past. does not matter.
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        private async Task UpdateLastCleanedUpTransactionId(long lastTranId)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "UPDATE dbo.Parameters SET Bigint = @LastTranId WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", LastCleanedUpTransactionId);
            cmd.Parameters.AddWithValue("@LastTranId", lastTranId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
