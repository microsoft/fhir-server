// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class WatchdogTimer : IDisposable
    {
        private bool _disposed = false;
        private Timer _timer;
        private bool _isRunning;

        protected WatchdogTimer()
        {
            _isRunning = false;
        }

        protected internal void StartTimer(double periodHour)
        {
            _timer = new Timer(_ => RunInternal(), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(10)), TimeSpan.FromHours(periodHour));
        }

        protected abstract void Run();

        private void RunInternal()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;
                Run();
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

            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            _disposed = true;
        }
    }
}
