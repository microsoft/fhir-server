// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Export
{
    internal class SqlService
    {
        private byte _partitionId;
        private object _partitioinLocker = new object();
        private byte _numberOfPartitions;
        private byte _queueType = 1;

        private string _connectionString;

        public SqlService(string connectionString)
        {
            _connectionString = connectionString;
            _numberOfPartitions = 16;
            _partitionId = 0;
        }

        public string ConnectionString => _connectionString;

        public void LogEvent(string process, string status, string mode, string target = null, string action = null, long? rows = null, DateTime? startTime = null, string text = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.LogEvent", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@Process", process);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Mode", mode);
            if (target != null)
            {
                command.Parameters.AddWithValue("@Target", target);
            }

            if (action != null)
            {
                command.Parameters.AddWithValue("@Action", action);
            }

            if (rows != null)
            {
                command.Parameters.AddWithValue("@Rows", rows);
            }

            if (startTime != null)
            {
                command.Parameters.AddWithValue("@Start", startTime);
            }

            if (text != null)
            {
                command.Parameters.AddWithValue("@Text", text);
            }

            command.ExecuteNonQuery();
        }

        public string ShowConnectionString()
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            return $"server={builder.DataSource};database={builder.InitialCatalog}";
        }

        private byte GetNextPartitionId(int? thread)
        {
            if (thread.HasValue)
            {
                return (byte)(thread.Value % _numberOfPartitions);
            }

            lock (_partitioinLocker)
            {
                _partitionId = _partitionId == _numberOfPartitions - 1 ? (byte)0 : ++_partitionId;
                return _partitionId;
            }
        }

        internal bool JobQueueIsNotEmpty()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.JobQueue WHERE QueueType = @QueueType", conn) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
            var cnt = (int)command.ExecuteScalar();
            return cnt > 0;
        }

        internal void DequeueJob(int? thread, out long groupId, out long jobId, out long version, out string definition)
        {
            definition = null;
            groupId = -1L;
            jobId = -1L;
            version = 0;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueJob", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
            command.Parameters.AddWithValue("@StartPartitionId", GetNextPartitionId(thread));
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            command.Parameters.AddWithValue("@HeartbeatTimeoutSec", 600);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                //// put job type here
                groupId = reader.GetInt64(0);
                jobId = reader.GetInt64(1);
                definition = reader.GetString(2);
                version = reader.GetInt64(3);
            }
        }

        internal void DequeueJob(int? thread, out long groupId, out long jobId, out long version, out short? resourceTypeId, out string minSurIdOrUrl, out string maxSurId)
        {
            DequeueJob(thread, out groupId, out jobId, out version, out var definition);
            resourceTypeId = null;
            minSurIdOrUrl = string.Empty;
            maxSurId = string.Empty;
            if (definition != null)
            {
                var split = definition.Split(";");
                resourceTypeId = short.Parse(split[0]);
                minSurIdOrUrl = split[1];
                maxSurId = split[2];
            }
        }

        internal void PutJobHeartbeat(long unitId, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobHeartbeat", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
            command.Parameters.AddWithValue("@JobId", unitId);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }

            command.ExecuteNonQuery();
        }

        internal void CompleteJob(long unitId, bool failed, long version, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
            command.Parameters.AddWithValue("@JobId", unitId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Failed", failed);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@Data", DBNull.Value);
            }

            command.Parameters.AddWithValue("@FinalResult", DBNull.Value);

            command.ExecuteNonQuery();
        }

        internal IEnumerable<byte[]> GetData(short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @$"
SELECT RawResource FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId AND IsHistory = 0",
                conn)
            { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return reader.GetSqlBytes(0).Value;
            }
        }
    }
}
