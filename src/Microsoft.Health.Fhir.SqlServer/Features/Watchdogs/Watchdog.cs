// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private double _periodSec;
        private double _leasePeriodSec;
        private readonly FhirTimer _fhirTimer;

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
            _logger.LogInformation($"{Name}.ExecuteAsync: starting...");

            await InitParamsAsync(PeriodSec, LeasePeriodSec);

            await Task.WhenAll(
                _fhirTimer.ExecuteAsync(_periodSec, OnNextTickAsync, cancellationToken),
                _watchdogLease.ExecuteAsync(AllowRebalance, _leasePeriodSec, cancellationToken));

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

            using (_logger.BeginTimedScope($"{Name}.OnNextTickAsync"))
            {
                await RunWorkAsync(cancellationToken);
            }
        }

        private async Task InitParamsAsync(double periodSec, double leasePeriodSec) // No CancellationToken is passed since we shouldn't cancel initialization.
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
                cmd.Parameters.AddWithValue("@PeriodSec", periodSec);
                cmd.Parameters.AddWithValue("@LeasePeriodSecId", LeasePeriodSecId);
                cmd.Parameters.AddWithValue("@LeasePeriodSec", leasePeriodSec);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);

                _periodSec = await GetPeriodAsync(CancellationToken.None);
                _leasePeriodSec = await GetLeasePeriodAsync(CancellationToken.None);

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
