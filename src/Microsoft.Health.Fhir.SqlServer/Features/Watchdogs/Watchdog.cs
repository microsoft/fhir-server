// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class Watchdog<T> : INotificationHandler<StorageInitializedNotification>
    {
        private readonly int? _minVersion;
        private readonly SchemaInformation _schemaInformation;
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly ILogger<T> _logger;

        private TimeSpan _timerDelay;
        private bool _storageReady;

        protected Watchdog(
            int? minVersion,
            Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ILogger<T> logger)
        {
            _minVersion = minVersion;
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        protected Watchdog()
        {
            // this is used to get param names for testing
        }

        internal string Name => GetType().Name;

        internal string IsEnabledId => $"{Name}.IsEnabled";

        internal string PeriodSecId => $"{Name}.PeriodSec";

        protected async Task InitializeAsync(bool isEnabled, int periodSec, CancellationToken cancellationToken)
        {
            // wait till we can truly init
            while (!_storageReady || _schemaInformation.Current < _minVersion)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            await InitParamsAsync(isEnabled, periodSec);
        }

        public async Task ExecutePeriodicLoopAsync(CancellationToken cancellationToken)
        {
            bool initialRun = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!initialRun)
                {
                    await Task.Delay(_timerDelay, cancellationToken);
                }
                else
                {
                    await Task.Delay(RandomDelay(), cancellationToken);
                }

                initialRun = false;

                if (cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                if (!await IsEnabledAsync(cancellationToken))
                {
                    _logger.LogInformation($"{Name} is disabled.");
                    continue;
                }

                try
                {
                    await ExecuteAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"{Name} failed.");
                }
            }

            _logger.LogInformation($"{Name} stopped.");
        }

        protected abstract Task ExecuteAsync(CancellationToken cancellationToken);

        private async Task InitParamsAsync(bool isEnabled, int periodSec) // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            _logger.LogInformation("InitParamsAsync starting...");

            // Offset for other instances running init
            await Task.Delay(RandomDelay(), CancellationToken.None);

            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, @IsEnabled
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, @PeriodSec
            ";
            cmd.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
            cmd.Parameters.AddWithValue("@PeriodSecId", PeriodSecId);
            cmd.Parameters.AddWithValue("@IsEnabled", isEnabled);
            cmd.Parameters.AddWithValue("@PeriodSec", periodSec);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            _timerDelay = TimeSpan.FromSeconds(await GetPeriodAsync(CancellationToken.None));

            _logger.LogInformation("InitParamsAsync completed.");
        }

        protected async Task<double> GetNumberParameterByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();

            cmd.CommandText = "SELECT Number FROM dbo.Parameters WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            var value = await cmd.ExecuteScalarAsync(cancellationToken);

            if (value == null)
            {
                throw new InvalidOperationException($"{id} is not set correctly in the Parameters table.");
            }

            return (double)value;
        }

        private async Task<double> GetPeriodAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(PeriodSecId, cancellationToken);
            return value;
        }

        private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(IsEnabledId, cancellationToken);
            return value == 1;
        }

        protected static TimeSpan RandomDelay()
        {
            return TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(10) / 10.0);
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
