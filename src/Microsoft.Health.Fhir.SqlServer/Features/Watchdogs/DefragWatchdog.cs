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

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public sealed class DefragWatchdog : Watchdog<DefragWatchdog>
    {
        private const byte QueueType = (byte)Core.Features.Operations.QueueType.Defrag;
        private int _threads;
        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;

        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly Func<IScoped<SqlQueueClient>> _sqlQueueClient;
        private readonly ILogger<DefragWatchdog> _logger;

        public DefragWatchdog(
            Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            Func<IScoped<SqlQueueClient>> sqlQueueClient,
            ILogger<DefragWatchdog> logger)
            : base(SchemaVersionConstants.Defrag, sqlConnectionWrapperFactory, schemaInformation, logger)
        {
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlQueueClient = EnsureArg.IsNotNull(sqlQueueClient, nameof(sqlQueueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal DefragWatchdog()
            : base()
        {
            // this is used to get param names for testing
        }

        internal string HeartbeatPeriodSecId => $"{Name}.HeartbeatPeriodSec";

        internal string HeartbeatTimeoutSecId => $"{Name}.HeartbeatTimeoutSec";

        internal string ThreadsId => $"{Name}.Threads";

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await InitializeAsync(false, 24 * 3600, cancellationToken);
            await InitDefragParamsAsync();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var job = await GetCoordinatorJobAsync(cancellationToken);

            if (job.jobId == -1)
            {
                _logger.LogInformation("Coordinator job was not found.");
                await Task.CompletedTask;
                return;
            }

            _logger.LogInformation("Group={GroupId} Job={JobId}: ActiveDefragItems={ActiveDefragItems}, executing...", job.groupId, job.jobId, job.activeDefragItems);

            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            await scopedQueueClient.Value.ExecuteJobWithHeartbeatAsync(
                QueueType,
                job.jobId,
                job.version,
                async cancellationSource =>
                {
                    try
                    {
                        var newDefragItems = await InitDefragAsync(job.groupId, cancellationSource);
                        _logger.LogInformation("Group={GroupId} Job={JobId}: NewDefragItems={NewDefragItems}.", job.groupId, job.jobId, newDefragItems);
                        if (job.activeDefragItems > 0 || newDefragItems > 0)
                        {
                            await ChangeDatabaseSettingsAsync(false, cancellationSource);

                            var tasks = new List<Task>();
                            for (var thread = 0; thread < _threads; thread++)
                            {
                                tasks.Add(ExecDefragWithHeartbeatAsync(cancellationSource));
                            }

                            await Task.WhenAll(tasks);
                            await ChangeDatabaseSettingsAsync(true, cancellationSource);

                            _logger.LogInformation("Group={GroupId} Job={JobId}: All ParallelTasks={ParallelTasks} tasks completed.", job.groupId, job.jobId, tasks.Count);
                        }
                        else
                        {
                            _logger.LogInformation("Group={GroupId} Job={JobId}: No defrag items found.", job.groupId, job.jobId);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "DefragWatchdog failed.");
                        throw;
                    }
                },
                TimeSpan.FromSeconds(_heartbeatPeriodSec),
                cancellationToken);

            await CompleteJobAsync(job.jobId, job.version, false, cancellationToken);
        }

        private async Task ChangeDatabaseSettingsAsync(bool isOn, CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.DefragChangeDatabaseSettings.PopulateCommand(cmd, isOn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("ChangeDatabaseSettings: {IsOn}.", isOn);
        }

        private async Task ExecDefragWithHeartbeatAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                (long groupId, long jobId, long version, string definition) job = await DequeueJobAsync(jobId: null, cancellationToken);

                long jobId = job.jobId;

                if (jobId == -1)
                {
                    return;
                }

                using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
                await scopedQueueClient.Value.ExecuteJobWithHeartbeatAsync(
                    QueueType,
                    jobId,
                    job.version,
                    async cancellationSource =>
                    {
                        var split = job.definition.Split(";");
                        await DefragAsync(split[0], split[1], int.Parse(split[2]), byte.Parse(split[3]) == 1, cancellationSource);
                    },
                    TimeSpan.FromSeconds(_heartbeatPeriodSec),
                    cancellationToken);

                await CompleteJobAsync(jobId, job.version, false, cancellationToken);
            }
        }

        private async Task DefragAsync(string table, string index, int partitionNumber, bool isPartitioned, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(table, nameof(table));
            EnsureArg.IsNotNullOrWhiteSpace(index, nameof(index));

            _logger.LogInformation("Beginning defrag on Table: {Table}, Index: {Index}, Partition: {PartitionNumber}", table, index, partitionNumber);

            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.Defrag.PopulateCommand(cmd, table, index, partitionNumber, isPartitioned);
            cmd.CommandTimeout = 0; // this is long running
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Finished defrag on Table: {Table}, Index: {Index}, Partition: {PartitionNumber}", table, index, partitionNumber);
        }

        private async Task CompleteJobAsync(long jobId, long version, bool failed, CancellationToken cancellationToken)
        {
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            var jobInfo = new JobInfo { QueueType = QueueType, Id = jobId, Version = version, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            await scopedQueueClient.Value.CompleteJobAsync(jobInfo, false, cancellationToken);

            _logger.LogInformation("Completed JobId: {JobId}, Version: {Version}, Failed: {Failed}", jobId, version, failed);
        }

        private async Task<int?> InitDefragAsync(long groupId, CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            var defragItems = 0;
            VLatest.InitDefrag.PopulateCommand(cmd, QueueType, groupId, defragItems);
            cmd.CommandTimeout = 0; // this is long running
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return VLatest.InitDefrag.GetOutputs(cmd);
        }

        private async Task<(long groupId, long jobId, long version, int activeDefragItems)> GetCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            var activeDefragItems = 0;
            using IScoped<SqlQueueClient> scopedQueueClient = _sqlQueueClient.Invoke();
            var queueClient = scopedQueueClient.Value;
            await queueClient.ArchiveJobsAsync(QueueType, cancellationToken);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                JobInfo job = (await queueClient.EnqueueAsync(QueueType, new[] { "Defrag" }, null, true, false, cancellationToken)).FirstOrDefault();

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
                var active = await GetActiveCoordinatorJobAsync(cancellationToken);
                id = (active.groupId, active.jobId, active.version);
                activeDefragItems = active.activeDefragItems;
            }

            if (id.jobId != -1)
            {
                var job = await DequeueJobAsync(id.jobId, cancellationToken);
                id = (job.groupId, job.jobId, job.version);
            }

            return (id.groupId, id.jobId, id.version, activeDefragItems);
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

        private async Task<(long groupId, long jobId, long version, int activeDefragItems)> GetActiveCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();

            // cannot use VLatest as it incorrectly asks for optional group id
            cmd.CommandText = "dbo.GetActiveJobs";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@QueueType", QueueType);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            var activeDefragItems = 0;
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(2) == "Defrag")
                {
                    id = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(3));
                }
                else
                {
                    activeDefragItems++;
                }
            }

            return (id.groupId, id.jobId, id.version, activeDefragItems);
        }

        private async Task<int> GetThreadsAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(ThreadsId, cancellationToken);
            return (int)value;
        }

        private async Task<int> GetHeartbeatPeriodAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(HeartbeatPeriodSecId, cancellationToken);
            return (int)value;
        }

        private async Task<int> GetHeartbeatTimeoutAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(HeartbeatTimeoutSecId, cancellationToken);
            return (int)value;
        }

        private async Task InitDefragParamsAsync() // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            _logger.LogInformation("InitDefragParamsAsync starting...");

            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%'
            ";
            cmd.Parameters.AddWithValue("@ThreadsId", ThreadsId);
            cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", HeartbeatPeriodSecId);
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", HeartbeatTimeoutSecId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            _threads = await GetThreadsAsync(CancellationToken.None);
            _heartbeatPeriodSec = await GetHeartbeatPeriodAsync(CancellationToken.None);
            _heartbeatTimeoutSec = await GetHeartbeatTimeoutAsync(CancellationToken.None);

            _logger.LogInformation("InitDefragParamsAsync completed.");
        }
    }
}
