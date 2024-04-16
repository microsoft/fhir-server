// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Operations
{
    [JobTypeId((int)JobType.SubscriptionsOrchestrator)]
    public class SubscriptionsOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly ITransactionDataStore _transactionDataStore;
        private readonly ISubscriptionManager _subscriptionManager;
        private const string OperationCompleted = "Completed";

        public SubscriptionsOrchestratorJob(
            IQueueClient queueClient,
            ITransactionDataStore transactionDataStore,
            ISubscriptionManager subscriptionManager)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(transactionDataStore, nameof(transactionDataStore));
            EnsureArg.IsNotNull(subscriptionManager, nameof(subscriptionManager));

            _queueClient = queueClient;
            _transactionDataStore = transactionDataStore;
            _subscriptionManager = subscriptionManager;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
            var resources = await _transactionDataStore.GetResourcesByTransactionIdAsync(definition.TransactionId, cancellationToken);

            var processingDefinition = new List<SubscriptionJobDefinition>();

            foreach (var sub in await _subscriptionManager.GetActiveSubscriptionsAsync(cancellationToken))
            {
                var chunk = resources
                    //// TODO: .Where(r => sub.FilterCriteria does something??);
                    .Chunk(sub.Channel.MaxCount);

                foreach (var batch in chunk)
                {
                    var cloneDefinition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
                    cloneDefinition.TypeId = (int)JobType.SubscriptionsProcessing;
                    cloneDefinition.ResourceReferences = batch.Select(r => new ResourceKey(r.ResourceTypeName, r.ResourceId, r.Version)).ToList();
                    cloneDefinition.Channel = sub.Channel;

                    processingDefinition.Add(cloneDefinition);
                }
            }

            if (processingDefinition.Count > 0)
            {
                await _queueClient.EnqueueAsync(QueueType.Subscriptions, cancellationToken, jobInfo.GroupId, definitions: processingDefinition.ToArray());
            }

            return OperationCompleted;
        }
    }
}
