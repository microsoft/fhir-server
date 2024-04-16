// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
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
        private readonly ISearchService _searchService;
        private readonly IQueryStringParser _queryStringParser;
        private readonly ISubscriptionManager _subscriptionManager;
        private const string OperationCompleted = "Completed";

        public SubscriptionsOrchestratorJob(
            IQueueClient queueClient,
            ITransactionDataStore transactionDataStore,
            ISearchService searchService,
            IQueryStringParser queryStringParser,
            ISubscriptionManager subscriptionManager)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(transactionDataStore, nameof(transactionDataStore));
            EnsureArg.IsNotNull(subscriptionManager, nameof(subscriptionManager));

            _queueClient = queueClient;
            _transactionDataStore = transactionDataStore;
            _searchService = searchService;
            _queryStringParser = queryStringParser;
            _subscriptionManager = subscriptionManager;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
            var resources = await _transactionDataStore.GetResourcesByTransactionIdAsync(definition.TransactionId, cancellationToken);
            var resourceKeys = resources.Select(r => new ResourceKey(r.ResourceTypeName, r.ResourceId, r.Version)).ToList().AsReadOnly();

            var processingDefinition = new List<SubscriptionJobDefinition>();

            foreach (var sub in await _subscriptionManager.GetActiveSubscriptionsAsync(cancellationToken))
            {
                var channelResources = new List<ResourceKey>();

                if (!string.IsNullOrEmpty(sub.FilterCriteria))
                {
                    var criteriaSegments = sub.FilterCriteria.Split('?');

                    List<Tuple<string, string>> query = new List<Tuple<string, string>>();

                    if (criteriaSegments.Length > 1)
                    {
                        query = _queryStringParser.Parse(criteriaSegments[1])
                            .Select(x => new Tuple<string, string>(x.Key, x.Value))
                            .ToList();
                    }

                    var limitIds = string.Join(",", resourceKeys.Select(x => x.Id));
                    var idParam = query.Where(x => x.Item1 == KnownQueryParameterNames.Id).FirstOrDefault();
                    if (idParam != null)
                    {
                        query.Remove(idParam);
                        limitIds += "," + idParam.Item2;
                    }

                    query.Add(new Tuple<string, string>(KnownQueryParameterNames.Id, limitIds));

                    var results = await _searchService.SearchAsync(criteriaSegments[0], new ReadOnlyCollection<Tuple<string, string>>(query), cancellationToken, true, ResourceVersionType.Latest, onlyIds: true);

                    channelResources.AddRange(
                        results.Results
                            .Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match
                                || x.SearchEntryMode == ValueSets.SearchEntryMode.Include)
                            .Select(x => x.Resource.ToResourceKey()));
                }
                else
                {
                    channelResources.AddRange(resourceKeys);
                }

                if (channelResources.Count == 0)
                {
                    continue;
                }

                var chunk = resourceKeys
                    .Chunk(sub.Channel.MaxCount);

                foreach (var batch in chunk)
                {
                    var cloneDefinition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
                    cloneDefinition.TypeId = (int)JobType.SubscriptionsProcessing;
                    cloneDefinition.ResourceReferences = batch.ToList();
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
