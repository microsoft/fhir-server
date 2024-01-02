// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public sealed class DefragWatchdog : Watchdog<DefragWatchdog>
    {
        private const byte QueueType = (byte)Core.Features.Operations.QueueType.Defrag;
        private int _threads;
        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;
        private CancellationToken _cancellationToken;
        private static readonly string[] Definitions = { "Defrag" };

        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlQueueClient _sqlQueueClient;
        private readonly ILogger<DefragWatchdog> _logger;

        public DefragWatchdog(
            ISqlRetryService sqlRetryService,
            SqlQueueClient sqlQueueClient,
            ILogger<DefragWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
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

        internal string IsEnabledId => $"{Name}.IsEnabled";

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await StartAsync(false, 24 * 3600, 2 * 3600, cancellationToken);
            await InitDefragParamsAsync();
        }

        protected override async Task ExecuteAsync()
        {
            if (!await IsEnabledAsync(_cancellationToken))
            {
                _logger.LogInformation("Watchdog is not enabled. Exiting...");
                return;
            }

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
            var job = await GetCoordinatorJobAsync(_cancellationToken);

            if (job.jobId == -1)
            {
                _logger.LogInformation("Coordinator job was not found.");
                return;
            }

            _logger.LogInformation("Group={GroupId} Job={JobId}: ActiveDefragItems={ActiveDefragItems}, executing...", job.groupId, job.jobId, job.activeDefragItems);

            await JobHosting.ExecuteJobWithHeartbeatsAsync(
                _sqlQueueClient,
                QueueType,
                job.jobId,
                job.version,
                async cancellationSource =>
                {
                    try
                    {
                        var newDefragItems = await InitDefragAsync(job.groupId, cancellationSource.Token);
                        _logger.LogInformation("Group={GroupId} Job={JobId}: NewDefragItems={NewDefragItems}.", job.groupId, job.jobId, newDefragItems);
                        if (job.activeDefragItems > 0 || newDefragItems > 0)
                        {
                            await ChangeDatabaseSettingsAsync(false, cancellationSource.Token);

                            var tasks = new List<Task>();
                            for (var thread = 0; thread < _threads; thread++)
                            {
                                tasks.Add(ExecDefragWithHeartbeatAsync(cancellationSource));
                            }

                            await Task.WhenAll(tasks);
                            await ChangeDatabaseSettingsAsync(true, cancellationSource.Token);

                            _logger.LogInformation("Group={GroupId} Job={JobId}: All ParallelTasks={ParallelTasks} tasks completed.", job.groupId, job.jobId, tasks.Count);
                        }
                        else
                        {
                            _logger.LogInformation("Group={GroupId} Job={JobId}: No defrag items found.", job.groupId, job.jobId);
                        }

                        return null;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "DefragWatchdog failed.");
                        throw;
                    }
                },
                TimeSpan.FromSeconds(_heartbeatPeriodSec),
                cancellationTokenSource);

            await CompleteJobAsync(job.jobId, job.version, false, _cancellationToken);
        }

        private async Task ChangeDatabaseSettingsAsync(bool isOn, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand("dbo.DefragChangeDatabaseSettings") { CommandType = CommandType.StoredProcedure};
            cmd.Parameters.AddWithValue("@IsOn", isOn);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            _logger.LogInformation("ChangeDatabaseSettings: {IsOn}.", isOn);
        }

        private async Task ExecDefragWithHeartbeatAsync(CancellationTokenSource cancellationTokenSource)
        {
            while (true)
            {
                (long groupId, long jobId, long version, string definition) job = await DequeueJobAsync(jobId: null, cancellationTokenSource.Token);

                long jobId = job.jobId;

                if (jobId == -1)
                {
                    return;
                }

                await JobHosting.ExecuteJobWithHeartbeatsAsync(
                    _sqlQueueClient,
                    QueueType,
                    jobId,
                    job.version,
                    async cancellationSource =>
                    {
                        var split = job.definition.Split(";");
                        await DefragAsync(split[0], split[1], int.Parse(split[2]), byte.Parse(split[3]) == 1, cancellationSource.Token);
                        return await Task.FromResult((string)null);
                    },
                    TimeSpan.FromSeconds(_heartbeatPeriodSec),
                    cancellationTokenSource);

                await CompleteJobAsync(jobId, job.version, false, cancellationTokenSource.Token);
            }
        }

        private async Task DefragAsync(string table, string index, int partitionNumber, bool isPartitioned, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(table, nameof(table));
            EnsureArg.IsNotNullOrWhiteSpace(index, nameof(index));

            _logger.LogInformation("Beginning defrag on Table: {Table}, Index: {Index}, Partition: {PartitionNumber}", table, index, partitionNumber);

            using var cmd = new SqlCommand("dbo.Defrag") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 }; // this is long running
            cmd.Parameters.AddWithValue("@TableName", table);
            cmd.Parameters.AddWithValue("@IndexName", index);
            cmd.Parameters.AddWithValue("@PartitionNumber", partitionNumber);
            cmd.Parameters.AddWithValue("@IsPartitioned", isPartitioned);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            _logger.LogInformation("Finished defrag on Table: {Table}, Index: {Index}, Partition: {PartitionNumber}", table, index, partitionNumber);
        }

        private async Task CompleteJobAsync(long jobId, long version, bool failed, CancellationToken cancellationToken)
        {
            var jobInfo = new JobInfo { QueueType = QueueType, Id = jobId, Version = version, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            await _sqlQueueClient.CompleteJobAsync(jobInfo, false, cancellationToken);

            _logger.LogInformation("Completed JobId: {JobId}, Version: {Version}, Failed: {Failed}", jobId, version, failed);
        }

        private async Task<int> InitDefragAsync(long groupId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand("dbo.InitDefrag") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 }; // this is long running
            cmd.Parameters.AddWithValue("@QueueType", QueueType);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            var defragItemsParam = new SqlParameter("@DefragItems", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(defragItemsParam);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            return (int)defragItemsParam.Value;
        }

        private async Task<(long groupId, long jobId, long version, int activeDefragItems)> GetCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            var activeDefragItems = 0;
            await _sqlQueueClient.ArchiveJobsAsync(QueueType, cancellationToken);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                var jobs = await _sqlQueueClient.EnqueueAsync(QueueType, Definitions, null, true, false, cancellationToken);

                if (jobs.Count > 0)
                {
                    id = (jobs[0].GroupId, jobs[0].Id, jobs[0].Version);
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
            JobInfo job = await _sqlQueueClient.DequeueAsync(QueueType, Environment.MachineName, _heartbeatTimeoutSec, cancellationToken, jobId);

            if (job != null)
            {
                return (job.GroupId, job.Id, job.Version, job.Definition);
            }

            return (-1, -1, -1, string.Empty);
        }

        private async Task<(long groupId, long jobId, long version, int activeDefragItems)> GetActiveCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand("dbo.GetActiveJobs") { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@QueueType", QueueType);
            (long groupId, long jobId, long version) id = (-1, -1, -1);
            var activeDefragItems = 0;
            await _sqlRetryService.ExecuteSql(
                cmd,
                async (command, cancel) =>
                {
                    await using var reader = await command.ExecuteReaderAsync(cancel);
                    while (await reader.ReadAsync(cancel))
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
                },
                _logger,
                null,
                cancellationToken);

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

            using var cmd = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%'
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 0
            ");
            cmd.Parameters.AddWithValue("@ThreadsId", ThreadsId);
            cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", HeartbeatPeriodSecId);
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", HeartbeatTimeoutSecId);
            cmd.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);

            _threads = await GetThreadsAsync(CancellationToken.None);
            _heartbeatPeriodSec = await GetHeartbeatPeriodAsync(CancellationToken.None);
            _heartbeatTimeoutSec = await GetHeartbeatTimeoutAsync(CancellationToken.None);

            _logger.LogInformation("InitDefragParamsAsync completed.");
        }

        private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        {
            var value = await GetNumberParameterByIdAsync(IsEnabledId, cancellationToken);
            return value == 1;
        }
    }
}
