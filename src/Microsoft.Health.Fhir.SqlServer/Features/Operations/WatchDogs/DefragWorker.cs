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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    public class DefragWorker : DefragTimer
    {
        private const byte _queueType = (byte)QueueType.Defrag;
        private int _threads;
        private const string _threadsId = "Defrag.Threads";
        private int _heartbeatPeriodSec;
        private const string _heartbeatPeriodSecId = "Defrag.HeartbeatPeriodSec";
        private int _heartbeatTimeoutSec;
        private const string _heartbeatTimeoutSecId = "Defrag.HeartbeatTimeoutSec";
        private double _periodHour;
        private const string _periodHourId = "Defrag.Period.Hours";
        private const string _isEnabledId = "Defrag.IsEnabled";
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private SchemaInformation _schemaInformation;
        private SqlQueueClient _sqlQueueClient;

        public DefragWorker(SqlConnectionWrapperFactory sqlConnectionWrapperFactory, SchemaInformation schemaInformation, SqlQueueClient sqlQueueClient)
        {
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _sqlQueueClient = sqlQueueClient;
        }

        public void Start()
        {
            InitParams();
            _threads = GetThreads();
            _heartbeatPeriodSec = GetHeartbeatPeriod();
            _heartbeatTimeoutSec = GetHeartbeatTimeout();
            _periodHour = GetPeriod();

            if (_schemaInformation.Current >= SchemaVersionConstants.Defrag && IsEnabled())
            {
                StartTimer(_periodHour);
            }
        }

        protected override void Run()
        {
            try
            {
                var id = GetCoordinatorJob();
                if (id.jobId == -1)
                {
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
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.InitDefrag.PopulateCommand(cmd, _queueType, groupId);
            cmd.CommandTimeout = 0; // this is long running
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
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
            var value = GetNumberParameterById(_threadsId);
            return (int)value;
        }

        private int GetHeartbeatPeriod()
        {
            var value = GetNumberParameterById(_heartbeatPeriodSecId);
            return (int)value;
        }

        private int GetHeartbeatTimeout()
        {
            var value = GetNumberParameterById(_heartbeatTimeoutSecId);
            return (int)value;
        }

        private double GetPeriod()
        {
            var value = GetNumberParameterById(_periodHourId);
            return (double)value;
        }

        private bool IsEnabled()
        {
            var value = GetNumberParameterById(_isEnabledId);
            return value == 1;
        }

        private static Task StartTask(Action action, bool longRunning = true)
        {
            var task = new Task(action, longRunning ? TaskCreationOptions.LongRunning : TaskCreationOptions.None);
            task.Start(TaskScheduler.Default);
            return task;
        }

        private void InitParams()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 0 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @IsEnabledId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @ThreadsId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodHourId, 24 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @PeriodHourId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 60 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatPeriodSecId)
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @HeartbeatTimeoutSecId)
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%' AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = name)
            ";
            cmd.Parameters.AddWithValue("@IsEnabledId", _isEnabledId);
            cmd.Parameters.AddWithValue("@ThreadsId", _threadsId);
            cmd.Parameters.AddWithValue("@PeriodHourId", _periodHourId);
            cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", _heartbeatPeriodSecId);
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", _heartbeatTimeoutSecId);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }
    }
}
