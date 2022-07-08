// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Store.Export
{
    internal class SqlService
    {
        private string _connectionString;
        private byte _queueType = (byte)QueueType.Export;

        public SqlService(string connectionString)
        {
            _connectionString = connectionString;
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

        internal bool JobQueueIsNotEmpty()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.JobQueue WHERE QueueType = @QueueType", conn) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
            var cnt = (int)command.ExecuteScalar();
            return cnt > 0;
        }

        internal void DequeueJob(out long groupId, out long jobId, out long version, out string definition)
        {
            definition = null;
            groupId = -1L;
            jobId = -1L;
            version = 0;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueJob", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", _queueType);
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

        internal void DequeueJob(out long groupId, out long jobId, out long version, out short? resourceTypeId, out string minSurIdOrUrl, out string maxSurId)
        {
            DequeueJob(out groupId, out jobId, out version, out var definition);
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
            command.Parameters.AddWithValue("@RequestCancellationOnFailure", true);
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

        internal IEnumerable<string> GetResourceIds(short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetResourcesByTypeAndSurrogateIdRange", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", minId);
            cmd.Parameters.AddWithValue("@EndId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return reader.GetString(1);
            }
        }

        internal IEnumerable<byte[]> GetDataBytes(short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetResourcesByTypeAndSurrogateIdRange", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", minId);
            cmd.Parameters.AddWithValue("@EndId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return reader.GetSqlBytes(0).Value;
            }
        }

        internal IEnumerable<(int UnitId, long StartId, long EndId, int ResourceCount)> GetSurrogateIdRanges(short resourceTypeId, long startId, long endId, int unitSize)
        {
            var numberOfRanges = 100;
            var returnedRanges = 101;
            var newStartId = startId;
            var iteration = 0;
            while (returnedRanges >= numberOfRanges)
            {
                returnedRanges = 0;
                var maxEndId = 0L;
                foreach (var range in GetSurrogateIdRanges(resourceTypeId, newStartId, endId, unitSize, numberOfRanges))
                {
                    returnedRanges++;
                    if (range.EndId > maxEndId)
                    {
                        maxEndId = range.EndId;
                    }

                    yield return range;
                }

                iteration++;
                newStartId = maxEndId + 1;
                Console.WriteLine($"GetSurrogateIdRanges.iteration={iteration}: returnedRanges={returnedRanges} newStartId={newStartId}");
            }
        }

        internal IEnumerable<(int UnitId, long StartId, long EndId, int ResourceCount)> GetSurrogateIdRanges(short resourceTypeId, long startId, long endId, int unitSize, int numberOfRanges)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetResourceSurrogateIdRanges", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 3600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", startId);
            cmd.Parameters.AddWithValue("@EndId", endId);
            cmd.Parameters.AddWithValue("@UnitSize", unitSize);
            cmd.Parameters.AddWithValue("@NumberOfRanges", numberOfRanges);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3));
            }
        }

        internal short GetResourceTypeId(string resourceType)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType B WHERE B.Name = @ResourceType", conn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@ResourceType", resourceType);
            using var reader = cmd.ExecuteReader();
            var resourceTypeId = (short)-1;
            while (reader.Read())
            {
                resourceTypeId = reader.GetInt16(0);
            }

            return resourceTypeId;
        }

        internal IEnumerable<string> GetDataStrings(short resourceTypeId, long minId, long maxId)
        {
            foreach (var res in GetDataBytes(resourceTypeId, minId, maxId))
            {
                using var mem = new MemoryStream(res);
                yield return CompressedRawResourceConverterCopy.ReadCompressedRawResource(mem);
            }
        }
    }
}
