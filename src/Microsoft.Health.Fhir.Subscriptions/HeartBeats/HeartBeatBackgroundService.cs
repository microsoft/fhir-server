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
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Validation;

namespace Microsoft.Health.Fhir.Subscriptions.HeartBeats
{
    public class HeartBeatBackgroundService : BackgroundService, INotificationHandler<StorageInitializedNotification>
    {
        private bool _storageReady = false;
        private readonly ILogger<HeartBeatBackgroundService> _logger;
        private readonly IScopeProvider<SubscriptionManager> _subscriptionManager;
        private readonly StorageChannelFactory _storageChannelFactory;

        public HeartBeatBackgroundService(ILogger<HeartBeatBackgroundService> logger, IScopeProvider<SubscriptionManager> subscriptionManager, StorageChannelFactory storageChannelFactory)
        {
            _logger = logger;
            _subscriptionManager = subscriptionManager;
            _storageChannelFactory = storageChannelFactory;
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!_storageReady)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            var nextHeartBeat = new Dictionary<string, DateTime>();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await periodicTimer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    // our logic
                    using IScoped<SubscriptionManager> subscriptionManager = _subscriptionManager.Invoke();
                    await subscriptionManager.Value.SyncSubscriptionsAsync(stoppingToken);

                    var activeSubscriptions = await subscriptionManager.Value.GetActiveSubscriptionsAsync(stoppingToken);
                    var subscriptionsWithHeartbeat = activeSubscriptions.Where(subscription => !subscription.Channel.HeartBeatPeriod.Equals(null));

                    foreach (var subscription in subscriptionsWithHeartbeat)
                    {
                        if (!nextHeartBeat.ContainsKey(subscription.ResourceId))
                        {
                            nextHeartBeat[subscription.ResourceId] = DateTime.Now;
                        }

                        // checks if datetime is after current time
                        if (nextHeartBeat.GetValueOrDefault(subscription.ResourceId) <= DateTime.Now)
                        {
                            var channel = _storageChannelFactory.Create(subscription.Channel.ChannelType);
                            try
                            {
                                await channel.PublishHeartBeatAsync(subscription);
                                nextHeartBeat[subscription.ResourceId] = nextHeartBeat.GetValueOrDefault(subscription.ResourceId).Add(subscription.Channel.HeartBeatPeriod);
                            }
                            catch (SubscriptionException)
                            {
                                await subscriptionManager.Value.MarkAsError(subscription, stoppingToken);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Error executing timer");
                }
            }
        }
    }
}
