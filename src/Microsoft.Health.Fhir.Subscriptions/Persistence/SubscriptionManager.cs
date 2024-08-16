// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public sealed class SubscriptionManager : ISubscriptionManager, INotificationHandler<StorageInitializedNotification>
    {
        private readonly IScopeProvider<IFhirDataStore> _dataStoreProvider;
        private readonly IScopeProvider<ISearchService> _searchServiceProvider;
        private List<SubscriptionInfo> _subscriptions = new List<SubscriptionInfo>();
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ILogger<SubscriptionManager> _logger;
        private readonly ISubscriptionModelConverter _subscriptionModelConverter;
        private static readonly object _lock = new object();

        public SubscriptionManager(
            IScopeProvider<IFhirDataStore> dataStoreProvider,
            IScopeProvider<ISearchService> searchServiceProvider,
            IResourceDeserializer resourceDeserializer,
            ILogger<SubscriptionManager> logger,
            ISubscriptionModelConverter subscriptionModelConverter)
        {
            _dataStoreProvider = EnsureArg.IsNotNull(dataStoreProvider, nameof(dataStoreProvider));
            _searchServiceProvider = EnsureArg.IsNotNull(searchServiceProvider, nameof(searchServiceProvider));
            _resourceDeserializer = resourceDeserializer;
            _logger = logger;
            _subscriptionModelConverter = subscriptionModelConverter;
        }

        public async Task SyncSubscriptionsAsync(CancellationToken cancellationToken)
        {
            // requested | active | error | off

            var updatedSubscriptions = new List<SubscriptionInfo>();

            using var search = _searchServiceProvider.Invoke();

            // Get all the active subscriptions
            var activeSubscriptions = await search.Value.SearchAsync(
                KnownResourceTypes.Subscription,
                [
                    Tuple.Create("status", "active"),
                ],
                cancellationToken);

            foreach (var param in activeSubscriptions.Results)
            {
                var resource = _resourceDeserializer.Deserialize(param.Resource);

                SubscriptionInfo info = _subscriptionModelConverter.Convert(resource);

                if (info == null)
                {
                    _logger.LogWarning("Subscription with id {SubscriptionId} is valid", resource.Id);
                    continue;
                }

                updatedSubscriptions.Add(info);
            }

            lock (_lock)
            {
                _subscriptions = updatedSubscriptions;
            }
        }

        public async Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync(CancellationToken cancellationToken)
        {
            if (_subscriptions.Count == 0)
            {
                await SyncSubscriptionsAsync(cancellationToken);
            }

            return _subscriptions;
        }

        public async Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            // Preload subscriptions when storage becomes available
            await SyncSubscriptionsAsync(cancellationToken);
        }

        /*
        public async Task MarkAsError(SubscriptionInfo subscription)
        {

        }
        */
    }
}
