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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class DefragWatchdog : Watchdog<DefragWatchdog>
    {
        private const byte QueueType = (byte)Core.Features.Operations.QueueType.Defrag;
        private int _threads;
        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;
        private static readonly string[] DefragCoord = { "Defrag" };

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

        public override double LeasePeriodSec { get; internal set; } = 2 * 3600;

        public override bool AllowRebalance { get; internal set; } = false;

        public override double PeriodSec { get; internal set; } = 24 * 3600;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            await ChangeDatabaseSettingsAsync(true, cancellationToken); // make sure that there are no leftovers from previous runs

            _threads = await GetThreadsAsync(CancellationToken.None); // renew on each exec

            if (!await IsEnabledAsync(cancellationToken))
            {
                _logger.LogInformation("DefragWatchdog is not enabled. Exiting...");
                return;
            }

            var coord = await GetCoordinatorJobAsync(cancellationToken);

            if (coord.jobId == -1)
            {
                _logger.LogInformation("DefragWatchdog.GetCoordinatorJobAsync: coordinator job was not found.");
                return;
            }

            _logger.LogInformation($"DefragWatchdog.coord={coord.jobId}: started.");

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await JobHosting.ExecuteJobWithHeartbeatsAsync(
                _sqlQueueClient,
                QueueType,
                coord.jobId,
                coord.version,
                async cancel =>
                {
                    try
                    {
                        var tables = await GetPartitionedTables(cancel.Token); // get tables
                        _logger.LogInformation($"DefragWatchdog.GetPartitionedTables.coord={coord.groupId}: tables={tables.Count}.");

                        // parallel loop on table level
                        await ChangeDatabaseSettingsAsync(false, cancel.Token);
                        await Parallel.ForEachAsync(tables, new ParallelOptions { MaxDegreeOfParallelism = _threads }, async (table, _) =>
                        {
                            var job = await _sqlQueueClient.EnqueueWithStatusAsync(QueueType, coord.groupId, table, JobStatus.Running, null, null, cancel.Token);
                            var items = (await GetFragmentation(table, null, null, cancel.Token)).ToDictionary(_ => (_.Table, _.Index, _.Partition), _ => _.Frag);
                            _logger.LogInformation($"DefragWatchdog.GetFragmentation.coord={job.GroupId}: job={job.Id} table={table} items={items.Count}.");
                            var existingItems = await GetExistingItems(job.Id, cancel.Token);
                            _logger.LogInformation($"DefragWatchdog.GetExistingItems.coord={job.GroupId}: job={job.Id} table={table} existingItems={existingItems.Count}.");
                            foreach (var item in items.Where(_ => !existingItems.Contains(GetItemDefinition(_.Key.Table, _.Key.Index, _.Key.Partition))))
                            {
                                _logger.LogInformation($"DefragWatchdog.ExecDefragItem.coord={job.GroupId}: job={job.Id} table={item.Key.Table} index={item.Key.Index} partition={item.Key.Partition} beforeFrag={item.Value} starting...");
                                await ExecDefragItem(job.Id, item.Key.Table, item.Key.Index, item.Key.Partition, cancel.Token);
                                _logger.LogInformation($"DefragWatchdog.ExecDefragItem.coord={job.GroupId}: job={job.Id} table={item.Key.Table} index={item.Key.Index} partition={item.Key.Partition} completed.");
                            }

                            await _sqlQueueClient.CompleteJobAsync(new JobInfo { QueueType = QueueType, Id = job.Id, Version = job.Version, Status = JobStatus.Completed }, false, cancel.Token);
                        });

                        return null;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "DefragWatchdog failed.");
                        await _sqlRetryService.TryLogEvent("DefragWatchdog", "Error", e.ToString(), null, cancel.Token);
                        throw;
                    }
                },
                TimeSpan.FromSeconds(_heartbeatPeriodSec),
                cancellationTokenSource);

            await ChangeDatabaseSettingsAsync(true, cancellationToken);
            await CompleteCoordAsync(coord.jobId, coord.version, false, cancellationToken);
        }

        private async Task CompleteCoordAsync(long jobId, long version, bool failed, CancellationToken cancellationToken)
        {
            var jobInfo = new JobInfo { QueueType = QueueType, Id = jobId, Version = version, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            await _sqlQueueClient.CompleteJobAsync(jobInfo, false, cancellationToken);
            _logger.LogInformation($"DefragWatchdog.CompleteCoordAsync.coord={jobId}: version={version} failed={failed}");
        }

        private async Task ChangeDatabaseSettingsAsync(bool isOn, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.DefragChangeDatabaseSettings") { CommandType = CommandType.StoredProcedure};
            cmd.Parameters.AddWithValue("@IsOn", isOn);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);

            _logger.LogInformation("DefragWatchdog.ChangeDatabaseSettings: {IsOn}.", isOn);
        }

        private async Task<IReadOnlyCollection<string>> GetPartitionedTables(CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.GetPartitionedTables") { CommandType = CommandType.StoredProcedure };
            List<string> results = null;
            await _sqlRetryService.ExecuteSql(
                cmd,
                async (command, cancel) =>
                {
                    results = new List<string>();
                    await using var reader = await command.ExecuteReaderAsync(cancel);
                    while (await reader.ReadAsync(cancel))
                    {
                        results.Add(reader.GetString(0));
                    }
                },
                _logger,
                null,
                cancellationToken);

            return results;
        }

        private static string GetItemDefinition(string table, string index, int partition)
        {
            return $"{table};{index};{partition}";
        }

        private async Task<IReadOnlyCollection<string>> GetExistingItems(long groupId, CancellationToken cancellationToken)
        {
            return (await _sqlQueueClient.GetJobByGroupIdAsync(QueueType, groupId, true, cancellationToken)).Select(_ => _.Definition).ToList();
        }

        private async Task<IReadOnlyCollection<(string Table, string Index, int Partition, double Frag)>> GetFragmentation(string tableName, string indexName, int? partitionNumber, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.DefragGetFragmentation") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 }; // this is long running
            cmd.Parameters.AddWithValue("@TableName", tableName);
            if (indexName != null)
            {
                cmd.Parameters.AddWithValue("@IndexName", indexName);
            }

            if (partitionNumber.HasValue)
            {
                cmd.Parameters.AddWithValue("@PartitionNumber", partitionNumber.Value);
            }

            List<(string Table, string Index, int Partition, double Frag)> results = null;
            await _sqlRetryService.ExecuteSql(
                cmd,
                async (command, cancel) =>
                {
                    results = new List<(string Table, string Index, int Partition, double Frag)>();
                    await using var reader = await command.ExecuteReaderAsync(cancel);
                    while (await reader.ReadAsync(cancel))
                    {
                        results.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDouble(3)));
                    }
                },
                _logger,
                null,
                cancellationToken);

            return results;
        }

        private async Task ExecDefragItem(long jobId, string table, string index, int partition, CancellationToken cancellationToken)
        {
            try
            {
                var st = DateTime.UtcNow;
                await DefragAsync(table, index, partition, cancellationToken);
                _logger.LogInformation($"DefragWatchdog.ExecDefragItem.DefragAsync: jobId={jobId} table={table} index={index} partition={partition} completed.");
                var frag = (await GetFragmentation(table, index, partition, cancellationToken)).First().Frag;
                await _sqlQueueClient.EnqueueWithStatusAsync(QueueType, jobId, GetItemDefinition(table, index, partition), JobStatus.Completed, frag.ToString(), st, cancellationToken);
                _logger.LogInformation($"DefragWatchdog.ExecDefragItem.GetFragmentation: jobId={jobId} table={table} index={index} partition={partition} afterFrag={frag} completed.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"DefragWatchdog.ExecDefragItem: jobId={jobId} table={table} index={index} partition={partition} failed.");
            }
        }

        private async Task DefragAsync(string table, string index, int partition, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.Defrag") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 }; // this is long running
            cmd.Parameters.AddWithValue("@TableName", table);
            cmd.Parameters.AddWithValue("@IndexName", index);
            cmd.Parameters.AddWithValue("@PartitionNumber", partition);
            cmd.Parameters.AddWithValue("@IsPartitioned", true);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        private async Task<(long groupId, long jobId, long version)> GetCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            await _sqlQueueClient.ArchiveJobsAsync(QueueType, cancellationToken);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                var jobs = await _sqlQueueClient.EnqueueAsync(QueueType, DefragCoord, null, true, cancellationToken);

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
            }

            if (id.jobId != -1)
            {
                var job = await DequeueJobAsync(id.jobId, cancellationToken);
                id = (job.groupId, job.jobId, job.version);
            }

            return (id.groupId, id.jobId, id.version);
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

        private async Task<(long groupId, long jobId, long version)> GetActiveCoordinatorJobAsync(CancellationToken cancellationToken)
        {
            var activeJobs = await _sqlQueueClient.GetActiveJobsByQueueTypeAsync(QueueType, false, cancellationToken);

            (long groupId, long jobId, long version) id = (-1, -1, -1);

            var defragJob = activeJobs.FirstOrDefault(job => job.Definition == "Defrag");
            if (defragJob != null)
            {
                id = (defragJob.GroupId, defragJob.Id, defragJob.Version);
            }

            return (id.groupId, id.jobId, id.version);
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

        protected override async Task InitAdditionalParamsAsync()
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
