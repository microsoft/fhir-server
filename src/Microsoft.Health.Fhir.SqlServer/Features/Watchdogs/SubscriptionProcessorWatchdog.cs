// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class SubscriptionProcessorWatchdog : Watchdog<SubscriptionProcessorWatchdog>
    {
        private readonly SqlStoreClient _store;
        private readonly ILogger<SubscriptionProcessorWatchdog> _logger;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly IQueueClient _queueClient;
        private CancellationToken _cancellationToken;

        public SubscriptionProcessorWatchdog(
            SqlStoreClient store,
            ISqlRetryService sqlRetryService,
            IQueueClient queueClient,
            ILogger<SubscriptionProcessorWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        internal string LastEventProcessedTransactionId => $"{Name}.{nameof(LastEventProcessedTransactionId)}";

        internal async Task StartAsync(CancellationToken cancellationToken, double? periodSec = null, double? leasePeriodSec = null, double? retentionPeriodDays = null)
        {
            _cancellationToken = cancellationToken;
            await InitLastProcessedTransactionId();
            await StartAsync(true, periodSec ?? 3, leasePeriodSec ?? 20, cancellationToken);
        }

        protected override async Task ExecuteAsync()
        {
            _logger.LogInformation($"{Name}: starting...");
            var lastTranId = await GetLastTransactionId();
            var visibility = await _store.MergeResourcesGetTransactionVisibilityAsync(_cancellationToken);

            _logger.LogInformation($"{Name}: last transaction={lastTranId} visibility={visibility}.");

            var transactionsToProcess = await _store.GetTransactionsAsync(lastTranId, visibility, _cancellationToken);
            _logger.LogDebug($"{Name}: found transactions={transactionsToProcess.Count}.");

            if (transactionsToProcess.Count == 0)
            {
                await UpdateLastEventProcessedTransactionId(visibility);
                _logger.LogInformation($"{Name}: completed. transactions=0.");
                return;
            }

            var transactionsToQueue = new List<SubscriptionJobDefinition>();

            foreach (var tran in transactionsToProcess.Where(x => x.VisibleDate.HasValue).OrderBy(x => x.TransactionId))
            {
                var jobDefinition = new SubscriptionJobDefinition(Core.Features.Operations.JobType.SubscriptionsOrchestrator)
                {
                    TransactionId = tran.TransactionId,
                    VisibleDate = tran.VisibleDate.Value,
                };

                transactionsToQueue.Add(jobDefinition);
            }

            await _queueClient.EnqueueAsync(QueueType.Subscriptions, cancellationToken: _cancellationToken, definitions: transactionsToQueue.ToArray());
            await UpdateLastEventProcessedTransactionId(transactionsToProcess.Max(x => x.TransactionId));

            _logger.LogInformation($"{Name}: completed. transactions={transactionsToProcess.Count}");
        }

        private async Task<long> GetLastTransactionId()
        {
            return await GetLongParameterByIdAsync(LastEventProcessedTransactionId, _cancellationToken);
        }

        private async Task InitLastProcessedTransactionId()
        {
            using var cmd = new SqlCommand("INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, 5105975696064002770"); // surrogate id for the past. does not matter.
            cmd.Parameters.AddWithValue("@Id", LastEventProcessedTransactionId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }

        private async Task UpdateLastEventProcessedTransactionId(long lastTranId)
        {
            using var cmd = new SqlCommand("UPDATE dbo.Parameters SET Bigint = @LastTranId WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", LastEventProcessedTransactionId);
            cmd.Parameters.AddWithValue("@LastTranId", lastTranId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);
        }
    }
}
