// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    public class DefragWorker : DefragTimer
    {
        private const byte _queueType = (byte)QueueType.Defrag;
        private int _threads;
        private int _heartbeatPeriodSec;
        private int _heartbeatTimeoutSec;
        private double _periodHour;
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private SchemaInformation _schemaInformation;

        public DefragWorker(SqlConnectionWrapperFactory sqlConnectionWrapperFactory, SchemaInformation schemaInformation)
        {
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
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
            cmd.CommandText = "dbo.DefragChangeDatabaseSettings";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@IsOn", isOn);
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
                        var tbl = split[0];
                        var ind = split[1];
                        var partNumber = int.Parse(split[2]);
                        var isPart = byte.Parse(split[3]) == 1;

                        using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
                        using var cmd = conn.CreateRetrySqlCommand();
                        cmd.CommandText = "dbo.Defrag";
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandTimeout = 0;
                        cmd.Parameters.AddWithValue("@TableName", tbl);
                        cmd.Parameters.AddWithValue("@IndexName", ind);
                        cmd.Parameters.AddWithValue("@PartitionNumber", partNumber);
                        cmd.Parameters.AddWithValue("@IsPartitioned", isPart);
                        cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
                    },
                    jobId,
                    job.version);

                CompleteJob(jobId, job.version, false);
            }
        }

        private void CompleteJob(long jobId, long version, bool failed)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.PutJobStatus";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@JobId", jobId);
            cmd.Parameters.AddWithValue("@Version", version);
            cmd.Parameters.AddWithValue("@Failed", failed);
            cmd.Parameters.AddWithValue("@Data", DBNull.Value);
            cmd.Parameters.AddWithValue("@FinalResult", DBNull.Value);
            cmd.Parameters.AddWithValue("@RequestCancellationOnFailure", false);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
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
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.PutJobHeartbeat";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 0;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@JobId", jobId);
            cmd.Parameters.AddWithValue("@Version", version);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void ArchiveJobs()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.ArchiveJobs";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void InitDefrag(long groupId)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.InitDefrag";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 0;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private (long groupId, long jobId, long version) GetCoordinatorJob()
        {
            ArchiveJobs();

            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.EnqueueJobs";
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@ForceOneActiveJobGroup", true);
            var stringListParam = new StringListTableValuedParameterDefinition("@Definitions");
            stringListParam.AddParameter(cmd.Parameters, new[] { new StringListRow("Defrag") });

            (long groupId, long jobId, long version) id = (-1, -1, -1);
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    id = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(3));
                }
            }
            catch (SqlException e)
            {
                if (!e.Message.Contains("There are other active job groups", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }

            id = GetActiveCoordinatorJob();
            if (id.jobId != -1)
            {
                var job = DequeueJob(id.jobId);
                id = (job.groupId, job.jobId, job.version);
            }

            return id;
        }

        private (long groupId, long jobId, long version, string definition) DequeueJob(long? jobId = null)
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.DequeueJob";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@InputJobId", jobId);
            cmd.Parameters.AddWithValue("@Worker", Environment.MachineName);
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSec", _heartbeatTimeoutSec);

            (long groupId, long jobId, long version, string) id = (-1, -1, -1, string.Empty);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                id = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(3), reader.GetString(2));
            }

            return id;
        }

        private (long groupId, long jobId, long version) GetActiveCoordinatorJob()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.GetActiveJobs";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
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

        private int GetThreads()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Defrag.Threads'";
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return value == null ? 1 : (int)value;
        }

        private int GetHeartbeatPeriod()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Defrag.HeartbeatPeriodSec'";
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return value == null ? 1 : (int)value;
        }

        private int GetHeartbeatTimeout()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Defrag.HeartbeatTimeoutSec'";
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return value == null ? 1 : (int)value;
        }

        private double GetPeriod()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.Period.Hours'";
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return value == null ? 24 : (double)value;
        }

        private bool IsEnabled()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Defrag.IsEnabled'";
            var flag = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return flag != null && (bool)flag;
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
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.IsEnabled', 0 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.IsEnabled')
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.Threads', 4 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.Threads')
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.Period.Hours', 24 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.Period.Hours')
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.HeartbeatPeriodSec', 60 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.HeartbeatPeriodSec')
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.HeartbeatTimeoutSec', 600 WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.HeartbeatTimeoutSec')
INSERT INTO dbo.Parameters (Id,Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p' AND name LIKE '%defrag%' AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = name)
            ";
            var flag = cmd.ExecuteNonQueryAsync(CancellationToken.None).Result;
        }
    }
}
