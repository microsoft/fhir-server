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
using Microsoft.Health.Fhir.Store.Copy; // change to perm
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    internal class DefragWorker
    {
        private const byte _queueType = 200;
        private int _threads;

        internal DefragWorker(string connStr)
        {
            SqlService = new SqlService(connStr);
            BatchExtensions.StartTask(() =>
            {
                while (true) // replace with true schedule
                {
                    if (IsEnabled())
                    {
                        Defrag();
                    }
                    else
                    {
                        SqlService.LogEvent($"Defrag", "Warn", string.Empty, "IsEnabled=false");
                    }

                    Thread.Sleep(60 * 1000);
                }
            });
        }

        public SqlService SqlService { get; private set; }

        private void Defrag()
        {
            _threads = GetThreads();
            SqlService.LogEvent($"Defrag", "Warn", string.Empty, $"Threads={_threads}");
            SqlService.ParallelForEachShard(
                shardId =>
                {
                    var connStr = SqlService.ShardletMap.Shards[shardId].ConnectionString;
                    SqlService.LogEvent($"Defrag.Start", "Warn", string.Empty, SqlService.ShowConnectionString(connStr));
                    Defrag(connStr);
                },
                null);
        }

        private void Defrag(string connectionString)
        {
            try
            {
                var id = GetDefragJob(connectionString);
                if (id.jobId == -1)
                {
                    return;
                }

                InitDefrag(connectionString, id);

                ChangeDatabaseSettings(connectionString, false);

                var tasks = new List<Task>();
                for (var thread = 0; thread < _threads; thread++)
                {
                    tasks.Add(BatchExtensions.StartTask(() => ExecDefrag(connectionString)));
                }

                Task.WaitAll(tasks.ToArray());

                ChangeDatabaseSettings(connectionString, true);
            }
            catch (SqlException e)
            {
                SqlService.LogEvent($"Defrag", "Error", SqlService.ShowConnectionString(connectionString), text: e.ToString());
            }
        }

        private static void ChangeDatabaseSettings(string connectionString, bool isOn)
        {
            using var conn = SqlService.GetConnection(connectionString);
            using var cmd = new SqlCommand("dbo.DefragChangeDatabaseSettings", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@IsOn", isOn);
            cmd.ExecuteNonQuery();
        }

        private void ExecDefrag(string connectionString)
        {
            long jobId = -2;
            while (jobId != -1)
            {
                SqlService.DequeueJob(_queueType, out jobId, out var version, out var def, null, connectionString);
                if (jobId == -1)
                {
                    return;
                }

                ExecWithHeartbeat(
                    () =>
                    {
                        var split = def.Split(";");
                        var tbl = split[0];
                        var ind = split[1];
                        var partNumber = int.Parse(split[2]);
                        var isPart = bool.Parse(split[3]);

                        using var conn = new SqlConnection(connectionString);
                        conn.Open();
                        using var cmd = new SqlCommand("dbo.Defrag", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
                        cmd.Parameters.AddWithValue("@TableName", tbl);
                        cmd.Parameters.AddWithValue("@IndexName", ind);
                        cmd.Parameters.AddWithValue("@PartitionNumber", partNumber);
                        cmd.Parameters.AddWithValue("@IsPartitioned", isPart);
                        cmd.ExecuteNonQuery();
                    },
                    jobId,
                    version,
                    connectionString);

                SqlService.CompleteJob(_queueType, jobId, version, false, connStr: connectionString);
            }
        }

        private void ExecWithHeartbeat(Action action, long jobId, long version, string connectionString)
        {
            var heartbeat = new Timer(_ => SqlService.PutJobHeartbeat(_queueType, jobId, version, connectionString), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(60)), TimeSpan.FromSeconds(60));
            try
            {
                action();
            }
            finally
            {
                heartbeat.Dispose();
            }
        }

        private void InitDefrag(string connectionString, (long groupId, long jobId, long version) id)
        {
            ExecWithHeartbeat(
                () =>
                {
                    using var conn = new SqlConnection(connectionString);
                    conn.Open();
                    using var cmd = new SqlCommand("dbo.InitDefrag", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
                    cmd.Parameters.AddWithValue("@GroupId", id.groupId);
                    cmd.Parameters.AddWithValue("@JobId", id.jobId);
                    cmd.Parameters.AddWithValue("@Version", id.version);
                    cmd.ExecuteNonQuery();
                },
                id.jobId,
                id.version,
                connectionString);
        }

        private (long groupId, long jobId, long version) GetDefragJob(string connectionString)
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.EnqueueJobs", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            cmd.Parameters.AddWithValue("@QueueType", _queueType);
            cmd.Parameters.AddWithValue("@ForceOneActiveJobGroup", true);
            var stringListParam = new SqlParameter { ParameterName = "@Definitions" };
            stringListParam.AddStringList(new[] { "Defrag" });
            cmd.Parameters.Add(stringListParam);

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
                SqlService.LogEvent("Defrag", "Warn", string.Empty, text: e.Message);
                if (!e.Message.Contains("There are other active job groups", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }

            id = GetInitDefragJob(connectionString);
            if (id.jobId != -1)
            {
                SqlService.DequeueJob(_queueType, out var groupId, out var jobId, out var version, out var _, id.jobId, connectionString);
                id = (groupId, jobId, version);
            }

            return id;
        }

        private static (long groupId, long jobId, long version) GetInitDefragJob(string connectionString)
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetActiveJobs", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
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
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Defrag.Threads'", conn);
            var threads = cmd.ExecuteScalar();
            return threads == null ? 1 : (int)threads;
        }

        private bool IsEnabled()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Defrag.IsEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }
    }
}
