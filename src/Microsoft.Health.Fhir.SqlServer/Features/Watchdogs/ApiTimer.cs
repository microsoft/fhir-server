// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal abstract class ApiTimer : IDisposable
    {
        private Timer _timer;

        private const double ZeroPeriod = 0.05;

        private volatile bool _isRunning;

        private volatile bool _isFailing;

        private volatile bool _isStarted;

        private volatile string _lastException;

        protected ApiTimer(int periodSec)
        {
            PeriodSec = periodSec;
            TruePeriodSec = PeriodSec == 0 ? ZeroPeriod : PeriodSec;
            _isFailing = false;
            _lastException = null;
            LastRunDateTime = DateTime.Parse("2017-12-01");
        }

        internal int PeriodSec { get; set; }

        internal double TruePeriodSec { get; set; }

        internal DateTime LastRunDateTime { get; private set; }

        internal bool IsRunning => _isRunning;

        internal bool IsFailing => _isFailing;

        internal bool IsStarted => _isStarted;

        internal string LastException => _lastException;

        protected internal virtual void Start()
        {
            Start(PeriodSec);
        }

        protected internal void Start(int periodSec)
        {
            PeriodSec = periodSec;
            TruePeriodSec = PeriodSec == 0 ? ZeroPeriod : PeriodSec;
            _timer = new Timer(_ => RunInternal(), null, TimeSpan.FromSeconds(GetDueTime()), TimeSpan.FromSeconds(TruePeriodSec));
            _isStarted = true;
        }

        private double GetDueTime()
        {
            return TruePeriodSec * RandomNumberGenerator.GetInt32(1000) / 1000;
        }

        protected abstract void Run();

        internal void RunInternal()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;
                Run();
                _isFailing = false;
                _lastException = null;
                LastRunDateTime = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                _isFailing = true;
                _lastException = e.ToString();
            }
            finally
            {
                _isRunning = false;
            }
        }

        protected internal virtual void Abort()
        {
            Dispose();
        }

        // TODO: Fix Dspose
        public virtual void Dispose()
        {
            _timer.Dispose();
            _timer = null;
        }
    }
}
