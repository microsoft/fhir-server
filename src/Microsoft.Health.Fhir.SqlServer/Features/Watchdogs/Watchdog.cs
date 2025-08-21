// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private DateTime _lastLog;

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

        public bool IsInitialized { get; private set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"{Name}.ExecuteAsync: starting...");

            await InitParamsAsync();

            await Task.WhenAll(
                _fhirTimer.ExecuteAsync(Name, PeriodSec, OnNextTickAsync, cancellationToken),
                _watchdogLease.ExecuteAsync($"{Name}Lease", AllowRebalance, LeasePeriodSec, cancellationToken));

            _logger.LogDebug($"{Name}.ExecuteAsync: completed.");
        }

        protected abstract Task RunWorkAsync(CancellationToken cancellationToken);

        private async Task OnNextTickAsync(CancellationToken cancellationToken)
        {
            if (!_watchdogLease.IsLeaseHolder)
            {
                _logger.LogDebug($"{Name}.OnNextTickAsync: Skipping because watchdog is not a lease holder.");
                return;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            await RunWorkAsync(cancellationToken);

            if (DateTime.UtcNow - _lastLog > TimeSpan.FromHours(1))
            {
                _lastLog = DateTime.UtcNow;
                _logger.LogInformation($"{Name}.OnNextTickAsync ran in {stopwatch.ElapsedMilliseconds}");
            }
            else
            {
                _logger.LogDebug($"{Name}.OnNextTickAsync ran in {stopwatch.ElapsedMilliseconds}");
            }
        }

        private async Task InitParamsAsync() // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            using (_logger.BeginTimedScope($"{Name}.InitParamsAsync"))
            {
                // Offset for other instances running init
                await Task.Delay(TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(10) / 10.0), CancellationToken.None);

                _lastLog = DateTime.UtcNow;

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

                IsInitialized = true;
            }
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
