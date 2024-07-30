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
        private static readonly object _lock = new object();
        private const string MetaString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription";
        private const string CriteriaString = "http://azurehealthcareapis.com/data-extentions/SubscriptionTopics/transactions";
        private const string CriteriaExtensionString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-filter-criteria";
        private const string ChannelTypeString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-channel-type";
        ////private const string AzureChannelTypeString = "http://azurehealthcareapis.com/data-extentions/subscription-channel-type";
        private const string PayloadTypeString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-payload-content";
        private const string MaxCountString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-max-count";

        public SubscriptionManager(
            IScopeProvider<IFhirDataStore> dataStoreProvider,
            IScopeProvider<ISearchService> searchServiceProvider,
            IResourceDeserializer resourceDeserializer,
            ILogger<SubscriptionManager> logger)
        {
            _dataStoreProvider = EnsureArg.IsNotNull(dataStoreProvider, nameof(dataStoreProvider));
            _searchServiceProvider = EnsureArg.IsNotNull(searchServiceProvider, nameof(searchServiceProvider));
            _resourceDeserializer = resourceDeserializer;
            _logger = logger;
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
                    Tuple.Create("status", "active,requested"),
                ],
                cancellationToken);

            foreach (var param in activeSubscriptions.Results)
            {
                var resource = _resourceDeserializer.Deserialize(param.Resource);

                SubscriptionInfo info = ConvertToInfo(resource);

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

        internal static SubscriptionInfo ConvertToInfo(ResourceElement resource)
        {
            var profile = resource.Scalar<string>("Subscription.meta.profile");

            if (profile != MetaString)
            {
                return null;
            }

            var criteria = resource.Scalar<string>($"Subscription.criteria");

            if (criteria != CriteriaString)
            {
                return null;
            }

            var criteriaExt = resource.Scalar<string>($"Subscription.criteria.extension.where(url = '{CriteriaExtensionString}').value");
            var channelTypeExt = resource.Scalar<string>($"Subscription.channel.type.extension.where(url = '{ChannelTypeString}').value.code");
            var payloadType = resource.Scalar<string>($"Subscription.channel.payload.extension.where(url = '{PayloadTypeString}').value");
            var maxCount = resource.Scalar<int?>($"Subscription.channel.extension.where(url = '{MaxCountString}').value");

            var channelInfo = new ChannelInfo
            {
                Endpoint = resource.Scalar<string>($"Subscription.channel.endpoint"),
                ChannelType = channelTypeExt switch
                {
                    "azure-storage" => SubscriptionChannelType.Storage,
                    "azure-lake-storage" => SubscriptionChannelType.DatalakeContract,
                    _ => SubscriptionChannelType.None,
                },
                ContentType = payloadType switch
                {
                    "full-resource" => SubscriptionContentType.FullResource,
                    "id-only" => SubscriptionContentType.IdOnly,
                    _ => SubscriptionContentType.Empty,
                },
                MaxCount = maxCount ?? 100,
            };

            var info = new SubscriptionInfo(criteriaExt, channelInfo);

            return info;
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
    }
}
