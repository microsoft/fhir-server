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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class WatchdogsBackgroundService : BackgroundService, INotificationHandler<StorageInitializedNotification>
    {
        private readonly CoreFeatureConfiguration _featureConfig;
        private bool _storageReady = false;
        private readonly DefragWatchdog _defragWatchdog;
        private readonly CleanupEventLogWatchdog _cleanupEventLogWatchdog;
        private readonly IScopeProvider<TransactionWatchdog> _transactionWatchdogProvider;
        private readonly InvisibleHistoryCleanupWatchdog _invisibleHistoryCleanupWatchdog;
        private readonly Lazy<SubscriptionProcessorWatchdog> _subscriptionProcessorWatchdog;

        public WatchdogsBackgroundService(
            DefragWatchdog defragWatchdog,
            CleanupEventLogWatchdog cleanupEventLogWatchdog,
            IScopeProvider<TransactionWatchdog> transactionWatchdog,
            InvisibleHistoryCleanupWatchdog invisibleHistoryCleanupWatchdog,
            Lazy<SubscriptionProcessorWatchdog> subscriptionProcessorWatchdog,
            IOptions<CoreFeatureConfiguration> featureConfig)
        {
            _defragWatchdog = EnsureArg.IsNotNull(defragWatchdog, nameof(defragWatchdog));
            _cleanupEventLogWatchdog = EnsureArg.IsNotNull(cleanupEventLogWatchdog, nameof(cleanupEventLogWatchdog));
            _transactionWatchdogProvider = EnsureArg.IsNotNull(transactionWatchdog, nameof(transactionWatchdog));
            _invisibleHistoryCleanupWatchdog = EnsureArg.IsNotNull(invisibleHistoryCleanupWatchdog, nameof(invisibleHistoryCleanupWatchdog));
            _subscriptionProcessorWatchdog = EnsureArg.IsNotNull(subscriptionProcessorWatchdog, nameof(subscriptionProcessorWatchdog));
            _featureConfig = EnsureArg.IsNotNull(featureConfig?.Value, nameof(featureConfig));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!_storageReady)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            using var continuationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using IScoped<TransactionWatchdog> transactionWatchdog = _transactionWatchdogProvider.Invoke();

            var tasks = new List<Task>
            {
                _defragWatchdog.ExecuteAsync(continuationTokenSource.Token),
                _cleanupEventLogWatchdog.ExecuteAsync(continuationTokenSource.Token),
                transactionWatchdog.Value.ExecuteAsync(continuationTokenSource.Token),
                _invisibleHistoryCleanupWatchdog.ExecuteAsync(continuationTokenSource.Token),
            };

            if (_featureConfig.SupportsSubscriptions)
            {
                tasks.Add(_subscriptionProcessorWatchdog.Value.ExecuteAsync(continuationTokenSource.Token));
            }

            await Task.WhenAny(tasks);

            if (!stoppingToken.IsCancellationRequested)
            {
                // If any of the watchdogs fail, cancel all the other watchdogs
                await continuationTokenSource.CancelAsync();
            }

            await Task.WhenAll(tasks);
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
