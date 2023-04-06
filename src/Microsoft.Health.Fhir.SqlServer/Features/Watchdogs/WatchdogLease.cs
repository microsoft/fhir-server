// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal class WatchdogLease<T> : FhirTimer<T>
    {
        private const double TimeoutFactor = 0.25;
        private readonly object _locker = new object();

        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly ILogger<T> _logger;
        private DateTime _leaseEndTime;
        private double _leaseTimeoutSec;
        private readonly string _worker;
        private CancellationToken _cancellationToken;
        private readonly string _watchdogName;
        private bool _allowRebalance;

        internal WatchdogLease(Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory, ILogger<T> logger)
            : base(logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _watchdogName = typeof(T).Name;
            _worker = $"{Environment.MachineName}.{Environment.ProcessId}";
            _logger.LogInformation($"WatchdogLease:Created lease object, worker=[{_worker}].");
        }

        protected internal string Worker => _worker;

        protected internal bool IsLeaseHolder
        {
            get
            {
                lock (_locker)
                {
                    return (DateTime.UtcNow - _leaseEndTime).TotalSeconds < _leaseTimeoutSec;
                }
            }
        }

        protected internal async Task StartAsync(bool allowRebalance, double periodSec, CancellationToken cancellationToken)
        {
            _logger.LogInformation("WatchdogLease.StartAsync: starting...");
            _allowRebalance = allowRebalance;
            _cancellationToken = cancellationToken;
            _leaseEndTime = DateTime.MinValue;
            _leaseTimeoutSec = (int)Math.Ceiling(periodSec * TimeoutFactor); // if it is rounded to 0 it causes problems in AcquireResourceLease logic.
            await StartAsync(periodSec, cancellationToken);
            _logger.LogInformation("WatchdogLease.StartAsync: completed.");
        }

        protected override async Task RunAsync()
        {
            _logger.LogInformation($"WatchdogLease.RunAsync: Starting acquire: resource=[{_watchdogName}] worker=[{_worker}] period={PeriodSec} timeout={_leaseTimeoutSec}...");

            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(_cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.AcquireWatchdogLease";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Watchdog", _watchdogName);
            cmd.Parameters.AddWithValue("@Worker", _worker);
            cmd.Parameters.AddWithValue("@AllowRebalance", _allowRebalance);
            cmd.Parameters.AddWithValue("@WorkerIsRunning", IsRunning);
            cmd.Parameters.AddWithValue("@ForceAcquire", false); // TODO: Provide ability to set. usefull for tests
            cmd.Parameters.AddWithValue("@LeasePeriodSec", PeriodSec + _leaseTimeoutSec);
            var leaseEndTimePar = cmd.Parameters.AddWithValue("@LeaseEndTime", DateTime.UtcNow);
            leaseEndTimePar.Direction = ParameterDirection.Output;
            var isAcquiredPar = cmd.Parameters.AddWithValue("@IsAcquired", false);
            isAcquiredPar.Direction = ParameterDirection.Output;
            var currentHolderPar = cmd.Parameters.Add("@CurrentLeaseHolder", SqlDbType.VarChar, 100);
            currentHolderPar.Direction = ParameterDirection.Output;

            await cmd.ExecuteNonQueryAsync(_cancellationToken);

            var leaseEndTime = (DateTime)leaseEndTimePar.Value;
            var isAcquired = (bool)isAcquiredPar.Value;
            var currentHolder = (string)currentHolderPar.Value;
            lock (_locker)
            {
                _leaseEndTime = isAcquired ? leaseEndTime : _leaseEndTime;
            }

            _logger.LogInformation($"WatchdogLease.RunAsync: Completed acquire: resource=[{_watchdogName}] worker=[{_worker}] period={PeriodSec} timeout={_leaseTimeoutSec} leaseEndTime=[{leaseEndTime:s}] isAcquired={isAcquired} currentHolder=[{currentHolder}].");
        }
    }
}
