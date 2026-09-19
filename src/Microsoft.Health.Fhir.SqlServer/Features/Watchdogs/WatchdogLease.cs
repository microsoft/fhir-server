// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class WatchdogLease<T>
        where T : Watchdog<T>
    {
        private const double TimeoutFactor = 0.25;
        private readonly object _locker = new();

        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger _logger;
        private DateTimeOffset _leaseEndTime;
        private double _leaseTimeoutSec;
        private readonly string _worker;
        private string _workerId;
        private readonly string _watchdogName;
        private bool _allowRebalance;
        private readonly FhirTimer _fhirTimer;

        public WatchdogLease(ISqlRetryService sqlRetryService, ILogger<T> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(logger, nameof(logger));
            _watchdogName = typeof(T).Name;
            _worker = $"{Environment.MachineName}.{Environment.ProcessId}";
            _workerId = _worker;
            _fhirTimer = new FhirTimer(logger);

            _logger = new PeriodicLogger<T>(logger, TimeSpan.FromMinutes(15));
            _logger.LogInformation($"WatchdogLease:Created lease object for {_watchdogName}, worker=[{_worker}].");
        }

        public string Worker => _worker;

        public bool IsLeaseHolder
        {
            get
            {
                lock (_locker)
                {
                    return (Clock.UtcNow - _leaseEndTime).TotalSeconds < _leaseTimeoutSec;
                }
            }
        }

        public bool IsRunning => _fhirTimer.IsRunning;

        public double PeriodSec => _fhirTimer.PeriodSec;

        public async Task ExecuteAsync(string name, bool allowRebalance, double periodSec, Guid guid, CancellationToken cancellationToken)
        {
            _logger.LogDebug("WatchdogLease.StartAsync: starting...");

            _allowRebalance = allowRebalance;
            _leaseEndTime = DateTimeOffset.MinValue;
            _leaseTimeoutSec = (int)Math.Ceiling(periodSec * TimeoutFactor); // if it is rounded to 0 it causes problems in AcquireResourceLease logic.
            _workerId = _worker + '.' + guid;

            await _fhirTimer.ExecuteAsync(name, periodSec, OnNextTickAsync, cancellationToken);

            _logger.LogDebug("WatchdogLease.StartAsync: completed.");
        }

        protected async Task OnNextTickAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"WatchdogLease.RunAsync: Starting acquire: resource=[{_watchdogName}] worker=[{_workerId}] period={_fhirTimer.PeriodSec} timeout={_leaseTimeoutSec}...");

            await using var cmd = new SqlCommand("dbo.AcquireWatchdogLease") { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Watchdog", _watchdogName);
            cmd.Parameters.AddWithValue("@Worker", _workerId);
            cmd.Parameters.AddWithValue("@AllowRebalance", _allowRebalance);
            cmd.Parameters.AddWithValue("@WorkerIsRunning", IsRunning);
            cmd.Parameters.AddWithValue("@ForceAcquire", false); // TODO: Provide ability to set. usefull for tests
            cmd.Parameters.AddWithValue("@LeasePeriodSec", PeriodSec + _leaseTimeoutSec);

            SqlParameter leaseEndTimePar = cmd.Parameters.AddWithValue("@LeaseEndTime", DateTime.UtcNow);
            leaseEndTimePar.Direction = ParameterDirection.Output;

            SqlParameter isAcquiredPar = cmd.Parameters.AddWithValue("@IsAcquired", false);
            isAcquiredPar.Direction = ParameterDirection.Output;

            SqlParameter currentHolderPar = cmd.Parameters.Add("@CurrentLeaseHolder", SqlDbType.VarChar, 100);
            currentHolderPar.Direction = ParameterDirection.Output;

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            var leaseEndTime = DateTime.SpecifyKind((DateTime)leaseEndTimePar.Value, DateTimeKind.Utc);
            var isAcquired = (bool)isAcquiredPar.Value;
            var currentHolder = (string)currentHolderPar.Value;

            lock (_locker)
            {
                _leaseEndTime = isAcquired ? leaseEndTime : _leaseEndTime;
            }

            _logger.LogInformation($"WatchdogLease.RunAsync: Completed acquire: resource=[{_watchdogName}] worker=[{_workerId}] period={PeriodSec} timeout={_leaseTimeoutSec} isAcquired={isAcquired} currentHolder=[{currentHolder}].");
        }
    }
}
