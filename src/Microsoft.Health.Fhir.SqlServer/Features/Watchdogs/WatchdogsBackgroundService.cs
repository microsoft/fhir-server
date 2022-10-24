// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// The background service used to host the <see cref="DefragWatchdog"/>.
    /// </summary>
    public class WatchdogsBackgroundService : BackgroundService////, INotificationHandler<StorageInitializedNotification>
    {
        private readonly DefragWatchdog _defrag;
        private Timer _startTimer;

        public WatchdogsBackgroundService(DefragWatchdog defrag)
        {
            EnsureArg.IsNotNull(defrag, nameof(defrag));
            _defrag = defrag;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // StartWatchdogs() should be excuted only once and then start timer shoukd be disposed
            _startTimer = new Timer(_ => StartWatchdogs(), null, TimeSpan.FromSeconds(2), TimeSpan.FromDays(30));
            await Task.CompletedTask;
        }

        ////public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        ////{
        ////    _defrag.Start();
        ////    return Task.CompletedTask;
        ////}

        private void StartWatchdogs()
        {
            _defrag.Start();
            _startTimer.Dispose();
        }
    }
}
