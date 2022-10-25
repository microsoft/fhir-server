// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class WatchdogsBackgroundService : BackgroundService
    {
        private readonly DefragWatchdog _defragWatchdog;

        public WatchdogsBackgroundService(DefragWatchdog defragWatchdog)
        {
            _defragWatchdog = EnsureArg.IsNotNull(defragWatchdog, nameof(defragWatchdog));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _defragWatchdog.Initialize(stoppingToken);
            await _defragWatchdog.ExecuteAsync(stoppingToken);
        }
    }
}
