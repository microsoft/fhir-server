// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class FhirTimer<T> : IDisposable
    {
        private Timer _timer;
        private bool _disposed = false;
        private bool _isRunning;
        private bool _isFailing;
        private bool _isStarted;
        private string _lastException;
        private readonly ILogger<T> _logger;
        private CancellationToken _cancellationToken;

        protected FhirTimer(ILogger<T> logger = null)
        {
            _logger = logger;
            _isFailing = false;
            _lastException = null;
            LastRunDateTime = DateTime.Parse("2017-12-01");
        }

        internal double PeriodSec { get; set; }

        internal DateTime LastRunDateTime { get; private set; }

        internal bool IsRunning => _isRunning;

        internal bool IsFailing => _isFailing;

        internal bool IsStarted => _isStarted;

        internal string LastException => _lastException;

        protected internal async Task StartAsync(double periodSec, CancellationToken cancellationToken)
        {
            PeriodSec = periodSec;
            _cancellationToken = cancellationToken;
            _timer = new Timer(async _ => await RunInternalAsync(), null, TimeSpan.FromSeconds(PeriodSec * RandomNumberGenerator.GetInt32(1000) / 1000), TimeSpan.FromSeconds(PeriodSec));
            _isStarted = true;
            await Task.CompletedTask;
        }

        protected abstract Task RunAsync();

        private async Task RunInternalAsync()
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;
                await RunAsync();
                _isFailing = false;
                _lastException = null;
                LastRunDateTime = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                try
                {
                    _logger.LogWarning(e.ToString()); // exceptions in logger should never bubble up
                }
                catch
                {
                }

                _isFailing = true;
                _lastException = e.ToString();
            }
            finally
            {
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _timer?.Dispose();
            }

            _disposed = true;
        }
    }
}
