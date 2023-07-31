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
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class Watchdog<T> : FhirTimer<T>
    {
        private ISqlRetryService _sqlRetryService;
        private readonly ILogger<T> _logger;
        private readonly WatchdogLease<T> _watchdogLease;
        private bool _disposed = false;
        private double _periodSec;
        private double _leasePeriodSec;

        protected Watchdog(ISqlRetryService sqlRetryService, ILogger<T> logger)
            : base(logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _watchdogLease = new WatchdogLease<T>(_sqlRetryService, _logger);
        }

        protected Watchdog()
        {
            // this is used to get param names for testing
        }

        internal string Name => GetType().Name;

        internal string PeriodSecId => $"{Name}.PeriodSec";

        internal string LeasePeriodSecId => $"{Name}.LeasePeriodSec";

        internal bool IsLeaseHolder => _watchdogLease.IsLeaseHolder;

        internal string LeaseWorker => _watchdogLease.Worker;

        internal double LeasePeriodSec => _watchdogLease.PeriodSec;

        protected internal async Task StartAsync(bool allowRebalance, double periodSec, double leasePeriodSec, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name}.StartAsync: starting...");
            await InitParamsAsync(periodSec, leasePeriodSec);
            await StartAsync(_periodSec, cancellationToken);
            await _watchdogLease.StartAsync(allowRebalance, _leasePeriodSec, cancellationToken);
            _logger.LogInformation($"{Name}.StartAsync: completed.");
        }

        protected abstract Task ExecuteAsync();

        protected override async Task RunAsync()
        {
            if (!_watchdogLease.IsLeaseHolder)
            {
                _logger.LogInformation($"{Name}.RunAsync: Skipping because watchdog is not a lease holder.");
                return;
            }

            _logger.LogInformation($"{Name}.RunAsync: Starting...");
            await ExecuteAsync();
            _logger.LogInformation($"{Name}.RunAsync: Completed.");
        }

        private async Task InitParamsAsync(double periodSec, double leasePeriodSec) // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            _logger.LogInformation($"{Name}.InitParamsAsync: starting...");

            // Offset for other instances running init
            await Task.Delay(TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(10) / 10.0), CancellationToken.None);

            using var cmd = new SqlCommand(@"
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

            _logger.LogInformation($"{Name}.InitParamsAsync: completed.");
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

            using var cmd = new SqlCommand("SELECT Number FROM dbo.Parameters WHERE Id = @Id");
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

            using var cmd = new SqlCommand("SELECT Bigint FROM dbo.Parameters WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", id);
            var value = await cmd.ExecuteScalarAsync(_sqlRetryService, _logger, cancellationToken);

            if (value == null)
            {
                throw new InvalidOperationException($"{id} is not set correctly in the Parameters table.");
            }

            return (long)value;
        }

        public new void Dispose()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _watchdogLease?.Dispose();
            }

            base.Dispose(disposing);

            _disposed = true;
        }
    }
}
