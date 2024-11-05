// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class FhirTimer(ILogger logger = null)
    {
        private bool _active;

        private bool _isFailing;

        public double PeriodSec { get; private set; }

        public DateTimeOffset LastRunDateTime { get; private set; } = DateTimeOffset.Parse("2017-12-01");

        public bool IsFailing => _isFailing;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Runs the execution of the timer until the <see cref="CancellationToken"/> is cancelled.
        /// </summary>
        public async Task ExecuteAsync(double periodSec, Func<CancellationToken, Task> onNextTick, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(onNextTick, nameof(onNextTick));
            PeriodSec = periodSec;

            if (_active)
            {
                throw new InvalidOperationException("Timer is already running");
            }

            _active = true;
            await Task.Delay(TimeSpan.FromSeconds(PeriodSec * RandomNumberGenerator.GetInt32(1000) / 1000), cancellationToken);
            using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(PeriodSec));

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await periodicTimer.WaitForNextTickAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        IsRunning = true;
                        await onNextTick(cancellationToken);
                        LastRunDateTime = Clock.UtcNow;
                        _isFailing = false;
                    }
                    catch (Exception e)
                    {
                        logger?.LogWarning(e, "Error executing timer");
                        _isFailing = true;
                    }
                }
            }
            finally
            {
                _active = false;
                IsRunning = false;
            }
        }
    }
}
