// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Polly;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public sealed class DefragWorker : IDisposable, INotificationHandler<StorageInitializedNotification>
    {
        private const byte QueueType = (byte)Core.Features.Operations.QueueType.Defrag;
        private const string PeriodHourId = "Defrag.Period.Hours";
        internal const string IsEnabledId = "Defrag.IsEnabled";
        private const string HeartbeatTimeoutSecId = "Defrag.HeartbeatTimeoutSec";
        private const string HeartbeatPeriodSecId = "Defrag.HeartbeatPeriodSec";
        private const string ThreadsId = "Defrag.Threads";

        private int _threads;
        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;
        private double _periodHour;

        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly Func<IScoped<SqlQueueClient>> _sqlQueueClient;
        private readonly ILogger<DefragWorker> _logger;

        private PeriodicTimer _periodicTimer;
        private bool _storageReady;

        public DefragWorker(
            Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            Func<IScoped<SqlQueueClient>> sqlQueueClient,
            ILogger<DefragWorker> logger)
        {
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlQueueClient = EnsureArg.IsNotNull(sqlQueueClient, nameof(sqlQueueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (!_storageReady || _schemaInformation.Current < SchemaVersionConstants.Defrag)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }

            await InitParamsAsync();

            _threads = await GetThreads(cancellationToken);
            _heartbeatPeriodSec = await GetHeartbeatPeriod(cancellationToken);
            _heartbeatTimeoutSec = await GetHeartbeatTimeout(cancellationToken);
            _periodHour = await GetPeriod(cancellationToken);
            _periodicTimer = new PeriodicTimer(TimeSpan.FromHours(_periodHour));
            bool initialRun = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!initialRun)
                {
                    await _periodicTimer.WaitForNextTickAsync(cancellationToken);
                }

                initialRun = false;

                if (!await IsEnabled(cancellationToken))
                {
                    _logger.LogInformation("DefragWorker is disabled.");
                    continue;
                }

                try
                {
                    (long groupId, long jobId, long version) id = await GetCoordinatorJob(cancellationToken);
                    if (id.jobId == -1)
                    {
                        continue;
                    }

                    _logger.LogInformation("DefragWorker found {JobId}, executing.", id.jobId);

                    await ExecWithHeartbeatAsync(
                        async cancellationSource =>
                        {
                            try
                            {
                                await InitDefragAsync(id.groupId, cancellationSource);
                                await ChangeDatabaseSettings(false, cancellationSource);

                                var tasks = new List<Task>();
                                for (var thread = 0; thread < _threads; thread++)
                                {
                                    tasks.Add(ExecDefrag(cancellationSource));
                                }

                                await Task.WhenAll(tasks);
                                await ChangeDatabaseSettings(true, cancellationSource);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "Defrag failed.");
                                throw;
                            }
                        },
                        id.jobId,
                        id.version,
                        cancellationToken);

                    await CompleteJob(id.jobId, id.version, false, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "DefragWorker failed");
                }
            }
        }

        private async Task ChangeDatabaseSettings(bool isOn, CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.DefragChangeDatabaseSettings.PopulateCommand(cmd, isOn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task ExecDefrag(CancellationToken cancellationToken)
        {
            var jobId = -2L;
            while (true)
            {
                (long groupId, long jobId, long version, string definition) job = await DequeueJobAsync(jobId, cancellationToken);

                jobId = job.jobId;

                if (jobId == -1)
                {
                    return;
                }

                await ExecWithHeartbeatAsync(
                    async cancellationSource =>
                    {
                        var split = job.definition.Split(";");
                        await DefragAsync(split[0], split[1], int.Parse(split[2]), byte.Parse(split[3]) == 1, cancellationSource);
                    },
                    jobId,
                    job.version,
                    cancellationToken);

                await CompleteJob(jobId, job.version, false, cancellationToken);
            }
        }

        private async Task DefragAsync(string table, string index, int partitionNumber, bool isPartitioned, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(table, nameof(table));
            EnsureArg.IsNotNullOrWhiteSpace(index, nameof(index));

            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.Defrag.PopulateCommand(cmd, table, index, partitionNumber, isPartitioned);
            cmd.CommandTimeout = 0; // this is long running
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task CompleteJob(long jobId, long version, bool failed, CancellationToken cancellationToken)
        {
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            var jobInfo = new JobInfo { QueueType = QueueType, Id = jobId, Version = version, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            await scopedQueueClient.Value.CompleteJobAsync(jobInfo, false, cancellationToken);
        }

        private async Task ExecWithHeartbeatAsync(Func<CancellationToken, Task> action, long jobId, long version, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_heartbeatPeriodSec));

            CancellationToken timerToken = tokenSource.Token;
            Task heartBeatTask = HeartbeatLoop(jobId, version, timer, timerToken);
            Task actionTask = action.Invoke(timerToken);

            await Task.WhenAny(actionTask, heartBeatTask);

            if (!timerToken.IsCancellationRequested && timerToken.CanBeCanceled)
            {
                tokenSource.Cancel(false);
                await heartBeatTask;
            }

            if (!actionTask.IsCompleted)
            {
                await actionTask;
            }
        }

        private async Task HeartbeatLoop(long jobId, long version, PeriodicTimer timer, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(timer, nameof(timer));

            while (!cancellationToken.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await PutJobHeartbeatAsync(jobId, version, cancellationToken);
            }
        }

        private async Task PutJobHeartbeatAsync(long jobId, long version, CancellationToken cancellationToken)
        {
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            var jobInfo = new JobInfo { QueueType = QueueType, Id = jobId, Version = version };
            await scopedQueueClient.Value.KeepAliveJobAsync(jobInfo, cancellationToken);
        }

        private async Task InitDefragAsync(long groupId, CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.InitDefrag.PopulateCommand(cmd, QueueType, groupId);
            cmd.CommandTimeout = 0; // this is long running
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<(long groupId, long jobId, long version)> GetCoordinatorJob(CancellationToken cancellationToken)
        {
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            await scopedQueueClient.Value.ArchiveJobsAsync(QueueType, cancellationToken);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                JobInfo job = (await scopedQueueClient.Value
                    .EnqueueAsync(QueueType, new[] { "Defrag" }, null, true, false, cancellationToken))
                    .FirstOrDefault();

                if (job != null)
                {
                    id = (job.GroupId, job.Id, job.Version);
                }
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("There are other active job groups", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }

            if (id.jobId == -1)
            {
                id = await GetActiveCoordinatorJobAsync(cancellationToken);
            }

            if (id.jobId != -1)
            {
                (long groupId, long jobId, long version, string definition) job = await DequeueJobAsync(id.jobId, cancellationToken);
                id = (job.groupId, job.jobId, job.version);
            }

            return id;
        }

        private async Task<(long groupId, long jobId, long version, string definition)> DequeueJobAsync(long? jobId = null, CancellationToken cancellationToken = default)
        {
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            JobInfo job = await scopedQueueClient.Value.DequeueAsync(QueueType, Environment.MachineName, _heartbeatTimeoutSec, cancellationToken, jobId);

            if (job != null)
            {
                return (job.GroupId, job.Id, job.Version, job.Definition);
            }

            return (-1, -1, -1, string.Empty);
        }

        private async Task<(long groupId, long jobId, long version)> GetActiveCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();

            // cannot use VLatest as it incorrectly asks for optional group id
            cmd.CommandText = "dbo.GetActiveJobs";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@QueueType", QueueType);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(2) == "Defrag")
                {
                    id = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(3));
                }
            }

            return id;
        }

        private async Task<double> GetNumberParameterById(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
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

        private async Task<int> GetThreads(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(ThreadsId, cancellationToken);
            return (int)value;
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
            var value = await GetNumberParameterById(PeriodHourId, cancellationToken);
            return (double)value;
        }

        private async Task<bool> IsEnabled(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterById(IsEnabledId, cancellationToken);
            return value == 1;
        }

        private async Task InitParamsAsync()
        {
            await Policy.Handle<SqlException>(e => !e.Message.Contains("login failed", StringComparison.OrdinalIgnoreCase) &&
                                                   !e.Message.Contains("dbo.Parameters", StringComparison.OrdinalIgnoreCase))
                .WaitAndRetryAsync(
                    3,
                    count => TimeSpan.FromSeconds(2),
                    (ex, count) => _logger.LogWarning(ex, "Failed to initialize parameters. Retrying..."))
                .ExecuteAsync(async () =>
                {
                    using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
                    using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory
                        .Value
                        .ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
                    using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();

                    cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 0 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @IsEnabledId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @ThreadsId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodHourId, 24 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @PeriodHourId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatPeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatTimeoutSecId)
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%' AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = name)
            ";

                    cmd.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
                    cmd.Parameters.AddWithValue("@ThreadsId", ThreadsId);
                    cmd.Parameters.AddWithValue("@PeriodHourId", PeriodHourId);
                    cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", HeartbeatPeriodSecId);
                    cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", HeartbeatTimeoutSecId);

                    await cmd.ExecuteNonQueryAsync(CancellationToken.None);
                });
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _periodicTimer?.Dispose();
        }
    }
}
