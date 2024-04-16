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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Operations
{
    [JobTypeId((int)JobType.SubscriptionsOrchestrator)]
    public class SubscriptionsOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly Func<IScoped<ISearchService>> _searchService;
        private const string OperationCompleted = "Completed";

        public SubscriptionsOrchestratorJob(
            IQueueClient queueClient,
            Func<IScoped<ISearchService>> searchService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _queueClient = queueClient;
            _searchService = searchService;
        }

        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();

            // Get and evaluate the active subscriptions ...

            // await _queueClient.EnqueueAsync(QueueType.Subscriptions, cancellationToken, jobInfo.GroupId, definitions: processingDefinition);

            return Task.FromResult(OperationCompleted);
        }
    }
}
