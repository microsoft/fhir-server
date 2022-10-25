// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class DefragWatchdog : WatchdogTimer
    {
        private const byte _queueType = (byte)QueueType.Defrag;
        private int _threads;
        public const string ThreadsId = "Defrag.Threads";
        private int _heartbeatPeriodSec;
        public const string HeartbeatPeriodSecId = "Defrag.HeartbeatPeriodSec";
        private int _heartbeatTimeoutSec;
        public const string HeartbeatTimeoutSecId = "Defrag.HeartbeatTimeoutSec";
        private double _periodSec;
        public const string PeriodSecId = "Defrag.PeriodSec";
        public const string IsEnabledId = "Defrag.IsEnabled";
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private SchemaInformation _schemaInformation;
        private SqlQueueClient _sqlQueueClient;
        private ILogger<DefragWatchdog> _logger;

        public DefragWatchdog(SchemaInformation schemaInformation, Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory, Func<IScoped<SqlQueueClient>> sqlQueueClient, ILogger<DefragWatchdog> logger)
        {
            _schemaInformation = schemaInformation;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory.Invoke().Value;
            _sqlQueueClient = sqlQueueClient.Invoke().Value;
            _logger = logger;
        }

        public void Start()
        {
            InitParams();
            StartTimer(_periodSec);
        }

        public void Change(double periodSec)
        {
            ChangeTimer(periodSec);
        }

        protected override void Run()
        {
            _logger.LogInformation("Run starting...");
            if (_schemaInformation.Current < SchemaVersionConstants.Defrag || !IsEnabled())
            {
                _logger.LogInformation($"Current schema = {_schemaInformation.Current} required schema = {SchemaVersionConstants.Defrag} or defrag is not enabled. Existing...");
                return;
            }

            try
            {
                var id = GetCoordinatorJob();
                if (id.jobId == -1)
                {
                    _logger.LogInformation("Cannot get coordinator job. Existing...");
                    return;
                }

                ExecWithHeartbeat(
                () =>
                {
                    try
                    {
                        InitDefrag(id.groupId);

                        ChangeDatabaseSettings(false);

                        var tasks = new List<Task>();
                        for (var thread = 0; thread < _threads; thread++)
                        {
                            tasks.Add(StartTask(() => ExecDefrag()));
                        }

                        Task.WaitAll(tasks.ToArray());

                        ChangeDatabaseSettings(true);
                        _logger.LogInformation("Defrag execution completed.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                },
                id.jobId,
                id.version);

                CompleteJob(id.jobId, id.version, false);
                _logger.LogInformation("Defrag coordinator job completed.");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void ChangeDatabaseSettings(bool isOn)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.DefragChangeDatabaseSettings.PopulateCommand(cmd, isOn);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void ExecDefrag()
        {
            var jobId = -2L;
            while (jobId != -1)
            {
                var job = DequeueJob();
                jobId = job.jobId;
                if (jobId == -1)
                {
                    return;
                }

                ExecWithHeartbeat(
                    () =>
                    {
                        var split = job.definition.Split(";");
                        Defrag(split[0], split[1], int.Parse(split[2]), byte.Parse(split[3]) == 1);
                    },
                    jobId,
                    job.version);

                CompleteJob(jobId, job.version, false);
            }
        }

        private void Defrag(string table, string index, int partitionNumber, bool isPartitioned)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.Defrag.PopulateCommand(cmd, table, index, partitionNumber, isPartitioned);
            cmd.CommandTimeout = 0; // this is long running
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void CompleteJob(long jobId, long version, bool failed)
        {
            var jobInfo = new JobInfo() { QueueType = _queueType, Id = jobId, Version = version, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            _sqlQueueClient.CompleteJobAsync(jobInfo, false, CancellationToken.None).Wait();
        }

        private void ExecWithHeartbeat(Action action, long jobId, long version)
        {
            var heartbeat = new Timer(_ => PutJobHeartbeat(jobId, version), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(_heartbeatPeriodSec)), TimeSpan.FromSeconds(_heartbeatPeriodSec));
            try
            {
                action();
            }
            finally
            {
                heartbeat.Dispose();
            }
        }

        private void PutJobHeartbeat(long jobId, long version)
        {
            var jobInfo = new JobInfo() { QueueType = _queueType, Id = jobId, Version = version };
            _sqlQueueClient.KeepAliveJobAsync(jobInfo, CancellationToken.None).Wait();
        }

        private void InitDefrag(long groupId)
        {
            _logger.LogInformation("InitDefrag starting...");
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.InitDefrag.PopulateCommand(cmd, _queueType, groupId);
            cmd.CommandTimeout = 0; // this is long running
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
            _logger.LogInformation("InitDefrag completed.");
        }

        private (long groupId, long jobId, long version) GetCoordinatorJob()
        {
            _sqlQueueClient.ArchiveJobs(_queueType);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                var job = _sqlQueueClient.EnqueueAsync(_queueType, new[] { "Defrag" }, null, true, false, CancellationToken.None).Result.FirstOrDefault();
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
                id = GetActiveCoordinatorJob();
            }

            if (id.jobId != -1)
            {
                var job = DequeueJob(id.jobId);
                id = (job.groupId, job.jobId, job.version);
            }

            return id;
        }

        private (long groupId, long jobId, long version, string definition) DequeueJob(long? jobId = null)
        {
            var job = _sqlQueueClient.DequeueAsync(_queueType, Environment.MachineName, _heartbeatTimeoutSec, CancellationToken.None, jobId).Result;
            if (job != null)
            {
                return (job.GroupId, job.Id, job.Version, job.Definition);
            }
            else
            {
                return (-1, -1, -1, string.Empty);
            }
        }

        private (long groupId, long jobId, long version) GetActiveCoordinatorJob()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();

            // cannot use VLatest as it incorrectly asks for optional group id
            cmd.CommandText = "dbo.GetActiveJobs";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(2) == "Defrag")
                {
                    id = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(3));
                }
            }

            return id;
        }

        private double GetNumberParameterById(string id)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT Number FROM dbo.Parameters WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            if (value == null)
            {
                throw new InvalidOperationException($"{id} is not set correctly in the Parameters table.");
            }

            return (double)value;
        }

        private int GetThreads()
        {
            var value = GetNumberParameterById(ThreadsId);
            return (int)value;
        }

        private int GetHeartbeatPeriod()
        {
            var value = GetNumberParameterById(HeartbeatPeriodSecId);
            return (int)value;
        }

        private int GetHeartbeatTimeout()
        {
            var value = GetNumberParameterById(HeartbeatTimeoutSecId);
            return (int)value;
        }

        private double GetPeriod()
        {
            var value = GetNumberParameterById(PeriodSecId);
            return (double)value;
        }

        private bool IsEnabled()
        {
            var value = GetNumberParameterById(IsEnabledId);
            return value == 1;
        }

        public static Task StartTask(Action action, bool longRunning = true)
        {
            var task = new Task(action, longRunning ? TaskCreationOptions.LongRunning : TaskCreationOptions.None);
            task.Start(TaskScheduler.Default);
            return task;
        }

        private void InitParams()
        {
            _logger.LogInformation("InitParams starting...");
        retry:
            try
            {
                using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
                using var cmd = conn.CreateRetrySqlCommand();
                cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 0 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @IsEnabledId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @ThreadsId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 24*3600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @PeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatPeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatTimeoutSecId)
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%' AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = name)
            ";
                cmd.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
                cmd.Parameters.AddWithValue("@ThreadsId", ThreadsId);
                cmd.Parameters.AddWithValue("@PeriodSecId", PeriodSecId);
                cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", HeartbeatPeriodSecId);
                cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", HeartbeatTimeoutSecId);
                cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
            }
            catch (SqlException e)
            {
                var str = e.ToString();
                if (str.Contains("login failed", StringComparison.OrdinalIgnoreCase) || str.Contains("dbo.Parameters", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"InitParams exception={str}.");
                    Thread.Sleep(2000);
                    goto retry;
                }

                _logger.LogError($"InitParams exception={str}.");
                throw;
            }

            _threads = GetThreads();
            _heartbeatPeriodSec = GetHeartbeatPeriod();
            _heartbeatTimeoutSec = GetHeartbeatTimeout();
            _periodSec = GetPeriod();
            _logger.LogInformation("InitParams completed.");
        }
    }
}
