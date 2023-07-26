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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class TransactionWatchdog : Watchdog<TransactionWatchdog>
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly IResourceWrapperFactory _factory;
        private readonly ILogger<TransactionWatchdog> _logger;
        private CancellationToken _cancellationToken;
        private const double _periodSec = 3;
        private const double _leasePeriodSec = 20;

        public TransactionWatchdog(SqlServerFhirDataStore store, IResourceWrapperFactory factory, ISqlRetryService sqlRetryService, ILogger<TransactionWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _factory = EnsureArg.IsNotNull(factory, nameof(factory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal TransactionWatchdog()
            : base()
        {
            // this is used to get param names for testing
        }

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await StartAsync(true, _periodSec, _leasePeriodSec, cancellationToken);
        }

        protected override async Task ExecuteAsync()
        {
            _logger.LogInformation("TransactionWatchdog starting...");
            var affectedRows = await _store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(_cancellationToken);
            _logger.LogInformation("TransactionWatchdog advanced visibility on {Transactions} transactions.", affectedRows);

            if (affectedRows > 0)
            {
                return;
            }

            var timeoutTransactions = await _store.StoreClient.MergeResourcesGetTimeoutTransactionsAsync((int)SqlServerFhirDataStore.MergeResourcesTransactionHeartbeatPeriod.TotalSeconds * 6, _cancellationToken);
            _logger.LogWarning("TransactionWatchdog found {Transactions} timed out transactions", timeoutTransactions.Count);
            if (timeoutTransactions.Count > 0)
            {
                await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"found timed out transactions={timeoutTransactions.Count}", null, _cancellationToken);
            }

            foreach (var tranId in timeoutTransactions)
            {
                var st = DateTime.UtcNow;
                _logger.LogInformation("TransactionWatchdog found timed out transaction={Transaction}, attempting to roll forward...", tranId);
                var resources = await _store.GetResourcesByTransactionIdAsync(tranId, _cancellationToken);
                if (resources.Count == 0)
                {
                    await _store.StoreClient.MergeResourcesCommitTransactionAsync(tranId, "WD: 0 resources", _cancellationToken);
                    _logger.LogWarning("TransactionWatchdog committed transaction={Transaction}, resources=0", tranId);
                    await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"committed transaction={tranId}, resources=0", st, _cancellationToken);
                    continue;
                }

                foreach (var resource in resources)
                {
                    _factory.Update(resource);
                }

                await _store.MergeResourcesWrapperAsync(tranId, false, resources.Select(_ => new MergeResourceWrapper(_, true, true)).ToList(), false, 0, _cancellationToken);
                await _store.StoreClient.MergeResourcesCommitTransactionAsync(tranId, null, _cancellationToken);
                _logger.LogWarning("TransactionWatchdog committed transaction={Transaction}, resources={Resources}", tranId, resources.Count);
                await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"committed transaction={tranId}, resources={resources.Count}", st, _cancellationToken);

                affectedRows = await _store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(_cancellationToken);
                _logger.LogInformation("TransactionWatchdog advanced visibility on {Transactions} transactions.", affectedRows);
            }
        }
    }
}
