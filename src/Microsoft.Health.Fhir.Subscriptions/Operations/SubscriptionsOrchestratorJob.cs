// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.InMemory;
using Microsoft.Health.Fhir.Core.Models;
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
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly IQueryStringParser _queryStringParser;
        private readonly ISubscriptionManager _subscriptionManager;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchIndexer _searchIndexer;
        private const string OperationCompleted = "Completed";

        public SubscriptionsOrchestratorJob(
            IQueueClient queueClient,
            ITransactionDataStore transactionDataStore,
            ISearchOptionsFactory searchOptionsFactory,
            IQueryStringParser queryStringParser,
            ISubscriptionManager subscriptionManager,
            IResourceDeserializer resourceDeserializer,
            ISearchIndexer searchIndexer)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(transactionDataStore, nameof(transactionDataStore));
            EnsureArg.IsNotNull(subscriptionManager, nameof(subscriptionManager));

            _queueClient = queueClient;
            _transactionDataStore = transactionDataStore;
            _searchOptionsFactory = searchOptionsFactory;
            _queryStringParser = queryStringParser;
            _subscriptionManager = subscriptionManager;
            _resourceDeserializer = resourceDeserializer;
            _searchIndexer = searchIndexer;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
            var resources = await _transactionDataStore.GetResourcesByTransactionIdAsync(definition.TransactionId, cancellationToken);
            var resourceKeys = resources.Select(r => new ResourceKey(r.ResourceTypeName, r.ResourceId, r.Version)).ToList().AsReadOnly();

            // Sync subscriptions if a change is detected
            if (resources.Any(x => string.Equals(x.ResourceTypeName, KnownResourceTypes.Subscription, StringComparison.Ordinal)))
            {
                await _subscriptionManager.SyncSubscriptionsAsync(cancellationToken);
            }

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

                    var searchOptions = _searchOptionsFactory.Create(criteriaSegments[0], query);
                    var searchInterpreter = new SearchQueryInterpreter();
                    var memoryIndex = new InMemoryIndex(_searchIndexer);
                    memoryIndex.IndexResources(resources.Select(x => _resourceDeserializer.Deserialize(x)).ToArray());
                    var expression = searchOptions.Expression;
                    var evaluator = expression.AcceptVisitor(searchInterpreter, default);
                    if (memoryIndex.Index.TryGetValue(criteriaSegments[0], out List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> value))
                    {
                        var results = evaluator.Invoke(value).ToArray();
                        channelResources.AddRange(results.Select(x => x.Location));
                    }
                }
                else
                {
                    channelResources.AddRange(resourceKeys);
                }

                if (channelResources.Count == 0)
                {
                    continue;
                }

                var chunk = channelResources
                    .Chunk(sub.Channel.MaxCount);

                foreach (var batch in chunk)
                {
                    var cloneDefinition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();
                    cloneDefinition.TypeId = (int)JobType.SubscriptionsProcessing;
                    cloneDefinition.ResourceReferences = batch.ToList();
                    cloneDefinition.SubscriptionInfo = sub;

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
