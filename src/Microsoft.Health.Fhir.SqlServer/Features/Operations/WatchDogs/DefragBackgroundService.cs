// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    /// <summary>
    /// The background service used to host the <see cref="DefragWorker"/>.
    /// </summary>
    public class DefragBackgroundService : BackgroundService
    {
        private readonly DefragWorker _defragWorker;
        private Timer _startTimer;

        public DefragBackgroundService(DefragWorker defragWorker)
        {
            EnsureArg.IsNotNull(defragWorker, nameof(defragWorker));
            _defragWorker = defragWorker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _startTimer = new Timer(_ => StartDefragWorker(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            await Task.CompletedTask;
        }

        private void StartDefragWorker()
        {
            _defragWorker.Start();
            _startTimer.Dispose();
        }
    }
}
