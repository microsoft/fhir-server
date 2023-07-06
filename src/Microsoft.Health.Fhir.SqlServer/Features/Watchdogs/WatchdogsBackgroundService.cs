// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class WatchdogsBackgroundService : BackgroundService, INotificationHandler<StorageInitializedNotification>
    {
        private bool _storageReady = false;
        private readonly DefragWatchdog _defragWatchdog;
        private readonly CleanupEventLogWatchdog _cleanupEventLogWatchdog;
        private readonly TransactionWatchdog _transactionWatchdog;

        public WatchdogsBackgroundService(DefragWatchdog defragWatchdog, CleanupEventLogWatchdog cleanupEventLogWatchdog, TransactionWatchdog transactionWatchdog)
        {
            _defragWatchdog = EnsureArg.IsNotNull(defragWatchdog, nameof(defragWatchdog));
            _cleanupEventLogWatchdog = EnsureArg.IsNotNull(cleanupEventLogWatchdog, nameof(cleanupEventLogWatchdog));
            _transactionWatchdog = EnsureArg.IsNotNull(transactionWatchdog, nameof(transactionWatchdog));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!_storageReady)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            await _defragWatchdog.StartAsync(stoppingToken);
            await _cleanupEventLogWatchdog.StartAsync(stoppingToken);
            await _transactionWatchdog.StartAsync(stoppingToken);

            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _defragWatchdog.Dispose();
            _cleanupEventLogWatchdog.Dispose();
            _transactionWatchdog.Dispose();
            base.Dispose();
        }
    }
}
