// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class FhirTimer<T>(ILogger<T> logger = null)
    {
        private bool _isFailing;

        internal double PeriodSec { get; set; }

        internal DateTimeOffset LastRunDateTime { get; private set; } = DateTime.Parse("2017-12-01");

        internal bool IsFailing => _isFailing;

        protected async Task StartAsync(double periodSec, CancellationToken cancellationToken)
        {
            PeriodSec = periodSec;

            await Task.Delay(TimeSpan.FromSeconds(PeriodSec * RandomNumberGenerator.GetInt32(1000) / 1000), cancellationToken);
            using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(PeriodSec));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await periodicTimer.WaitForNextTickAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Time to exit
                    break;
                }

                try
                {
                    await RunAsync();
                    LastRunDateTime = Clock.UtcNow;
                    _isFailing = false;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Error executing timer");
                    _isFailing = true;
                }
            }
        }

        protected abstract Task RunAsync();
    }
}
