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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class TransactionWatchdog : Watchdog<TransactionWatchdog>
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly IResourceWrapperFactory _factory;
        private readonly ILogger<TransactionWatchdog> _logger;
        private const string AdvancedVisibilityTemplate = "TransactionWatchdog advanced visibility on {Transactions} transactions.";

        public TransactionWatchdog(SqlServerFhirDataStore store, IResourceWrapperFactory factory, ISqlRetryService sqlRetryService, ILogger<TransactionWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _factory = EnsureArg.IsNotNull(factory, nameof(factory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal TransactionWatchdog()
        {
            // this is used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 20;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 3;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            var affectedRows = await _store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(cancellationToken);

            _logger.Log(
                affectedRows > 0 ? LogLevel.Information : LogLevel.Debug,
                AdvancedVisibilityTemplate,
                affectedRows);

            if (affectedRows > 0)
            {
                return;
            }

            IReadOnlyList<long> timeoutTransactions = await _store.StoreClient.MergeResourcesGetTimeoutTransactionsAsync((int)SqlServerFhirDataStore.MergeResourcesTransactionHeartbeatPeriod.TotalSeconds * 6, cancellationToken);
            if (timeoutTransactions.Count > 0)
            {
                _logger.LogWarning("TransactionWatchdog found {Transactions} timed out transactions", timeoutTransactions.Count);
                await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"found timed out transactions={timeoutTransactions.Count}", null, cancellationToken);
            }
            else
            {
                _logger.Log(
                    timeoutTransactions.Count > 0 ? LogLevel.Information : LogLevel.Debug,
                    "TransactionWatchdog found {Transactions} timed out transactions",
                    timeoutTransactions.Count);
            }

            foreach (var tranId in timeoutTransactions)
            {
                var st = DateTime.UtcNow;
                _logger.LogInformation("TransactionWatchdog found timed out transaction={Transaction}, attempting to roll forward...", tranId);
                var resources = await _store.GetResourcesByTransactionIdAsync(tranId, cancellationToken);
                if (resources.Count == 0)
                {
                    await _store.StoreClient.MergeResourcesCommitTransactionAsync(tranId, "WD: 0 resources", cancellationToken);
                    _logger.LogWarning("TransactionWatchdog committed transaction={Transaction}, resources=0", tranId);
                    await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"committed transaction={tranId}, resources=0", st, cancellationToken);
                    continue;
                }

                foreach (var resource in resources)
                {
                    _factory.Update(resource);
                }

                await _store.MergeResourcesWrapperAsync(tranId, false, resources.Select(x => new MergeResourceWrapper(x, true, true)).ToList(), false, 0, cancellationToken);
                await _store.StoreClient.MergeResourcesCommitTransactionAsync(tranId, null, cancellationToken);
                _logger.LogWarning("TransactionWatchdog committed transaction={Transaction}, resources={Resources}", tranId, resources.Count);
                await _store.StoreClient.TryLogEvent("TransactionWatchdog", "Warn", $"committed transaction={tranId}, resources={resources.Count}", st, cancellationToken);

                affectedRows = await _store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(cancellationToken);
                _logger.LogInformation("TransactionWatchdog advanced visibility on {Transactions} transactions.", affectedRows);
            }
        }
    }
}
