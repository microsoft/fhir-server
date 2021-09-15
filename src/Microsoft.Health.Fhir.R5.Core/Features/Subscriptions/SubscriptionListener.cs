// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public class SubscriptionListener : ISubscriptionListener
    {
        private List<Subscription> _subscriptions;
        private Dictionary<string, SubscriptionTopic> _subscriptionTopics;
        private ISearchService _searchService;
        private IChannelManager _channelManager;
        private IResourceDeserializer _resourceDeserializer;

        private bool _initalized = false;

        public SubscriptionListener(ISearchService searchService, IChannelManager channelManager, IResourceDeserializer resourceDeserializer)
        {
            // Requries a search operator
            // Search for all subscriptions
            // Add them to the array
            // May need to move this to an intitalize method due to async nature of search

            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(channelManager, nameof(channelManager));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            _searchService = searchService;
            _channelManager = channelManager;
            _resourceDeserializer = resourceDeserializer;

            _subscriptions = new List<Subscription>();
            _subscriptionTopics = new Dictionary<string, SubscriptionTopic>();
        }

        private async System.Threading.Tasks.Task Initialize()
        {
            var results = await _searchService.SearchAsync("Subscription", new List<Tuple<string, string>>(), CancellationToken.None);
            foreach (var result in results.Results)
            {
                var instance = _resourceDeserializer.Deserialize(result.Resource).ToPoco<Subscription>();
                if (instance.Status == SubscriptionState.Active)
                {
                    _subscriptions.Add(instance);
                }

                // need support for continuation tokens
            }

            results = await _searchService.SearchAsync("SubscriptionTopic", new List<Tuple<string, string>>(), CancellationToken.None);
            foreach (var result in results.Results)
            {
                var instance = _resourceDeserializer.Deserialize(result.Resource).ToPoco<SubscriptionTopic>();
                _subscriptionTopics.Add(instance.Url, instance);

                // need support for continuation tokens
            }

            _initalized = true;
        }

        public bool TryAddListener(Resource resource)
        {
            if (resource.TypeName.Equals("Subscription", StringComparison.OrdinalIgnoreCase))
            {
                AddListener((Subscription)resource);
                return true;
            }

            return false;
        }

        public void AddListener(Subscription subscription)
        {
            _subscriptions.Add(subscription);
        }

        public async System.Threading.Tasks.Task Evaluate(Resource resource, SubscriptionTopic.InteractionTrigger interaction)
        {
            if (!_initalized)
            {
                await Initialize();
            }

            foreach (Subscription subscription in _subscriptions)
            {
                bool potentialTrigger = false;

                if (_subscriptionTopics.TryGetValue(subscription.Topic, out var subscriptionTopic))
                {
                    foreach (SubscriptionTopic.ResourceTriggerComponent trigger in subscriptionTopic.ResourceTrigger)
                    {
                        if (trigger.Description.Equals(resource.TypeName, StringComparison.OrdinalIgnoreCase)
                            && trigger.MethodCriteria.Contains(interaction))
                        {
                            potentialTrigger = true;
                        }
                    }
                }

                if (potentialTrigger)
                {
                    INotificationChannel channel = null;
                    if (subscription.ChannelType.Code.Equals("rest-hook", StringComparison.OrdinalIgnoreCase))
                    {
                        channel = _channelManager.GetRestHookNotificationChannel();
                    }

                    switch (subscription.Content)
                    {
                        case Subscription.SubscriptionPayloadContent.Empty:
                            channel.NotifiyEmpty(subscription);
                            break;
                        case Subscription.SubscriptionPayloadContent.IdOnly:
                            channel.NotifiyIdOnly(subscription, new Resource[] { resource });
                            break;
                        case Subscription.SubscriptionPayloadContent.FullResource:
                            channel.NotifiyFullResource(subscription, new Resource[] { resource });
                            break;
                    }
                }
            }
        }
    }
}
