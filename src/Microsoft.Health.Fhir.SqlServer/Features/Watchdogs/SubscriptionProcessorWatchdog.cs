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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly CoreFeatureConfiguration _config;

        public SubscriptionProcessorWatchdog(
            SqlStoreClient store,
            ISqlRetryService sqlRetryService,
            IQueueClient queueClient,
            IOptions<CoreFeatureConfiguration> coreConfiguration,
            ILogger<SubscriptionProcessorWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _config = EnsureArg.IsNotNull(coreConfiguration?.Value, nameof(coreConfiguration));
        }

        internal string LastEventProcessedTransactionId => $"{Name}.{nameof(LastEventProcessedTransactionId)}";

        public override double LeasePeriodSec { get; internal set; } = 20;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 3;

        protected override async Task InitAdditionalParamsAsync()
        {
            await InitLastProcessedTransactionId();
        }

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name}: starting...");
            var lastTranId = await GetLastTransactionId(cancellationToken);
            var visibility = await _store.MergeResourcesGetTransactionVisibilityAsync(cancellationToken);

            _logger.LogInformation($"{Name}: last transaction={lastTranId} visibility={visibility}.");

            var transactionsToProcess = await _store.GetTransactionsAsync(lastTranId, visibility, cancellationToken);
            _logger.LogDebug($"{Name}: found transactions={transactionsToProcess.Count}.");

            if (transactionsToProcess.Count == 0)
            {
                await UpdateLastEventProcessedTransactionId(visibility);
                _logger.LogInformation($"{Name}: completed. transactions=0.");
                return;
            }

            if (_config.SupportsSubscriptions)
            {
                var transactionsToQueue = new List<SubscriptionJobDefinition>();

                foreach (var tran in transactionsToProcess.Where(x => x.VisibleDate.HasValue).OrderBy(x => x.TransactionId))
                {
                    var jobDefinition = new SubscriptionJobDefinition(JobType.SubscriptionsOrchestrator)
                    {
                        TransactionId = tran.TransactionId,
                        VisibleDate = tran.VisibleDate.Value,
                    };

                    transactionsToQueue.Add(jobDefinition);
                }

                await _queueClient.EnqueueAsync(QueueType.Subscriptions, cancellationToken: cancellationToken, definitions: transactionsToQueue.ToArray());
            }

            await UpdateLastEventProcessedTransactionId(transactionsToProcess.Max(x => x.TransactionId));

            _logger.LogInformation($"{Name}: completed. transactions={transactionsToProcess.Count}");
        }

        private async Task<long> GetLastTransactionId(CancellationToken cancellationToken)
        {
            return await GetLongParameterByIdAsync(LastEventProcessedTransactionId, cancellationToken);
        }

        private async Task InitLastProcessedTransactionId()
        {
            using var cmd = new SqlCommand("INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, @LastTranId");
            cmd.Parameters.AddWithValue("@Id", LastEventProcessedTransactionId);
            cmd.Parameters.AddWithValue("@LastTranId", await _store.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None));
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
