// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal abstract class Watchdog<T>
        where T : Watchdog<T>
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<T> _logger;
        private readonly WatchdogLease<T> _watchdogLease;
        private readonly FhirTimer _fhirTimer;
        private bool _supportsLastRunTime;
        private bool _useLastRunTime;

        protected Watchdog(ISqlRetryService sqlRetryService, ILogger<T> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _watchdogLease = new WatchdogLease<T>(_sqlRetryService, _logger);
            _fhirTimer = new FhirTimer(_logger);
        }

        protected Watchdog()
        {
            // this is used to get param names for testing
        }

        public string Name => GetType().Name;

        public string PeriodSecId => $"{Name}.PeriodSec";

        public string LeasePeriodSecId => $"{Name}.LeasePeriodSec";

        public bool IsLeaseHolder => _watchdogLease.IsLeaseHolder;

        public string LeaseWorker => _watchdogLease.Worker;

        public abstract double LeasePeriodSec { get; internal set; }

        public abstract bool AllowRebalance { get; internal set; }

        public abstract double PeriodSec { get; internal set; }

        public bool AllowDbPeriodOverride { get; internal set; } = true;

        public bool IsInitialized { get; private set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name}.ExecuteAsync: starting...");

            await InitParamsAsync();

            if (PeriodSec > LeasePeriodSec) // only use last run time logic for large periods.
            {
                _useLastRunTime = true;
            }

            await Task.WhenAll(
                _fhirTimer.ExecuteAsync(Name, GetPeriod(PeriodSec, LeasePeriodSec), OnNextTickAsync, cancellationToken),
                _watchdogLease.ExecuteAsync($"{Name}Lease", AllowRebalance, LeasePeriodSec, cancellationToken));

            _logger.LogInformation($"{Name}.ExecuteAsync: completed.");
        }

        protected abstract Task RunWorkAsync(CancellationToken cancellationToken);

        private async Task OnNextTickAsync(CancellationToken cancellationToken)
        {
            if (!_watchdogLease.IsLeaseHolder)
            {
                _logger.LogDebug($"{Name}.OnNextTickAsync: Skipping because watchdog is not a lease holder.");
                return;
            }

            _supportsLastRunTime = await SupportsLastRunTimeAsync(cancellationToken);

            if (!await IsRunDueAsync(cancellationToken))
            {
                _logger.LogDebug($"{Name}.OnNextTickAsync: Skipping because the most recent run is newer than the watchdog period.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            await RunWorkAsync(cancellationToken);
            await UpdateLastRunTimeAsync(cancellationToken);

            _logger.LogInformation($"{Name}.OnNextTickAsync ran in milliseconds={stopwatch.ElapsedMilliseconds}");
        }

        internal static bool ShouldRun(DateTime utcNow, DateTime lastRunTime, TimeSpan period)
        {
            return utcNow - lastRunTime >= period;
        }

        internal static double GetPeriod(double periodSec, double leasePeriodSec)
        {
            return Math.Min(periodSec, leasePeriodSec);
        }

        private async Task<bool> IsRunDueAsync(CancellationToken cancellationToken)
        {
            var lastRunTime = await GetLastRunTimeAsync(cancellationToken);
            return ShouldRun(DateTime.UtcNow, lastRunTime, TimeSpan.FromSeconds(PeriodSec));
        }

        private async Task<bool> SupportsLastRunTimeAsync(CancellationToken cancellationToken)
        {
            if (!_useLastRunTime)
            {
                return false;
            }

            await using var cmd = new SqlCommand("SELECT CASE WHEN object_id('dbo.GetWatchdogLeasesLastRunTime') IS NOT NULL THEN 1 ELSE 0 END");
            var result = (int)await cmd.ExecuteScalarAsync(_sqlRetryService, _logger, cancellationToken);
            return result == 1;
        }

        private async Task<DateTime> GetLastRunTimeAsync(CancellationToken cancellationToken)
        {
            if (!_useLastRunTime || !_supportsLastRunTime)
            {
                return DateTime.MinValue;
            }

            await using var cmd = new SqlCommand("dbo.GetWatchdogLeasesLastRunTime") { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Watchdog", Name);

            var lastRunTime = cmd.Parameters.Add("@LastRunTime", SqlDbType.DateTime);
            lastRunTime.Direction = ParameterDirection.Output;

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            var value = lastRunTime.Value;
            if (value == null || value == DBNull.Value)
            {
                return DateTime.MinValue;
            }

            return (DateTime)value;
        }

        private async Task UpdateLastRunTimeAsync(CancellationToken cancellationToken)
        {
            if (!_useLastRunTime || !_supportsLastRunTime)
            {
                return;
            }

            await using var cmd = new SqlCommand("dbo.UpdateWatchdogLeasesLastRunTime") { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Watchdog", Name);
            cmd.Parameters.AddWithValue("@LastRunTime", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        private async Task InitParamsAsync() // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            if (AllowDbPeriodOverride)
            {
                using (_logger.BeginTimedScope($"{Name}.InitParamsAsync"))
                {
                    // Offset for other instances running init
                    await Task.Delay(TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(10) / 10.0), CancellationToken.None);

                    await using var cmd = new SqlCommand(
                        @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, @PeriodSec
INSERT INTO dbo.Parameters (Id,Number) SELECT @LeasePeriodSecId, @LeasePeriodSec
                        ");
                    cmd.Parameters.AddWithValue("@PeriodSecId", PeriodSecId);
                    cmd.Parameters.AddWithValue("@PeriodSec", PeriodSec);
                    cmd.Parameters.AddWithValue("@LeasePeriodSecId", LeasePeriodSecId);
                    cmd.Parameters.AddWithValue("@LeasePeriodSec", LeasePeriodSec);
                    await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);

                    PeriodSec = await GetPeriodAsync(CancellationToken.None);
                    LeasePeriodSec = await GetLeasePeriodAsync(CancellationToken.None);

                    await InitAdditionalParamsAsync();
                }
            }

            IsInitialized = true;
        }

        protected virtual Task InitAdditionalParamsAsync()
        {
            return Task.CompletedTask;
        }

        private async Task<double> GetPeriodAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(PeriodSecId, cancellationToken);
            return value;
        }

        private async Task<double> GetLeasePeriodAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(LeasePeriodSecId, cancellationToken);
            return value;
        }

        protected async Task<double> GetNumberParameterByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            await using var cmd = new SqlCommand("SELECT Number FROM dbo.Parameters WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", id);
            var value = await cmd.ExecuteScalarAsync(_sqlRetryService, _logger, cancellationToken);

            if (value == null)
            {
                throw new InvalidOperationException($"{id} is not set correctly in the Parameters table.");
            }

            return (double)value;
        }

        protected async Task<long> GetLongParameterByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            await using var cmd = new SqlCommand("SELECT Bigint FROM dbo.Parameters WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", id);
            var value = await cmd.ExecuteScalarAsync(_sqlRetryService, _logger, cancellationToken);

            if (value == null)
            {
                throw new InvalidOperationException($"{id} is not set correctly in the Parameters table.");
            }

            return (long)value;
        }
    }
}
