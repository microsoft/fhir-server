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
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public abstract class Watchdog<T> : INotificationHandler<StorageInitializedNotification>
    {
        internal const string PeriodSecId = "PeriodSec";
        internal const string IsEnabledId = "IsEnabled";
        internal const string HeartbeatTimeoutSecId = "HeartbeatTimeoutSec";
        internal const string HeartbeatPeriodSecId = "HeartbeatPeriodSec";

        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;
        private double _periodSec;

        private readonly string _name;
        private readonly int? _minVersion;
        private readonly SchemaInformation _schemaInformation;
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly Func<IScoped<SqlQueueClient>> _sqlQueueClient;
        private readonly ILogger<T> _logger;

        private TimeSpan _timerDelay;
        private bool _storageReady;

        protected Watchdog(
            string name,
            int? minVersion,
            SchemaInformation schemaInformation,
            Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory,
            Func<IScoped<SqlQueueClient>> sqlQueueClient,
            ILogger<T> logger)
        {
            _name = name;
            _minVersion = minVersion;
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlQueueClient = EnsureArg.IsNotNull(sqlQueueClient, nameof(sqlQueueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal void ChangeDelay(TimeSpan newTimerDelay)
        {
            _timerDelay = newTimerDelay;
        }

        public async Task Initialize(CancellationToken cancellationToken)
        {
            // wait till we can truly init
            while (!_storageReady || _schemaInformation.Current < _minVersion)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            await InitParamsAsync();

            _timerDelay = TimeSpan.FromSeconds(_periodSec);
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
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

                if (!await IsEnabled(cancellationToken))
                {
                    _logger.LogInformation("Watchdog is disabled.");
                    continue;
                }

                try
                {
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "DefragWatchdog failed.");
                }
            }

            _logger.LogInformation("DefragWatchdog stopped.");
        }

        private async Task<double> GetNumberParameterById(string id, CancellationToken cancellationToken)
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

        private async Task<int> GetHeartbeatPeriod(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(HeartbeatPeriodSecId, cancellationToken);
            return (int)value;
        }

        private async Task<int> GetHeartbeatTimeout(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(HeartbeatTimeoutSecId, cancellationToken);
            return (int)value;
        }

        private async Task<double> GetPeriod(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(PeriodSecId, cancellationToken);
            return value;
        }

        private async Task<bool> IsEnabled(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(IsEnabledId, cancellationToken);
            return value == 1;
        }

        private async Task InitParamsAsync()
        {
            // No CancellationToken is passed since we shouldn't cancel initialization.

            _logger.LogInformation("InitParams starting...");

            // Offset for other instances running init
            await Task.Delay(RandomDelay(), CancellationToken.None);

            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();

            cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 0 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @IsEnabledId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @ThreadsId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 24*3600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @PeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatPeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatTimeoutSecId)
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%' AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = name)
            ";

            cmd.Parameters.AddWithValue("@IsEnabledId", $"{_name}.{IsEnabledId}");
            cmd.Parameters.AddWithValue("@PeriodSecId", $"{_name}.{PeriodSecId}");
            cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", $"{_name}.{HeartbeatPeriodSecId}");
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", $"{_name}.{HeartbeatTimeoutSecId}");

            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            _heartbeatPeriodSec = await GetHeartbeatPeriod(CancellationToken.None);
            _heartbeatTimeoutSec = await GetHeartbeatTimeout(CancellationToken.None);
            _periodSec = await GetPeriod(CancellationToken.None);

            _logger.LogInformation("InitParams completed.");
        }

        private static TimeSpan RandomDelay()
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
