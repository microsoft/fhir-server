// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    /// <summary>
    /// Handles all communication between the API and SQL Server.
    /// </summary>
    public partial class SqlService
    {
        public const byte QueueType = 3;

        public bool JobQueueIsNotEmpty()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.JobQueue WHERE QueueType = @QueueType", conn) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            var cnt = (int)command.ExecuteScalar();
            return cnt > 0;
        }

        public void DequeueJob(out long groupId, out long jobId, out long version, out string definition)
        {
            definition = null;
            groupId = -1L;
            jobId = -1L;
            version = 0;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueJob", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
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

        public void DequeueJob(out long jobId, out long version, out short? resourceTypeId, out long minSurId, out long maxSurId)
        {
            DequeueJob(out var _, out jobId, out version, out var definition);
            resourceTypeId = null;
            minSurId = 0;
            maxSurId = 0;
            if (definition != null)
            {
                var split = definition.Split(";");
                resourceTypeId = short.Parse(split[0]);
                minSurId = long.Parse(split[1]);
                maxSurId = long.Parse(split[2]);
            }
        }

        public void PutJobHeartbeat(long jobId, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobHeartbeat", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }

            command.ExecuteNonQuery();
        }

        public void CompleteJob(long jobId, bool failed, long version, int? resourceCount = null, int? totalCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            command.Parameters.AddWithValue("@JobId", jobId);
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

            if (totalCount.HasValue)
            {
                command.Parameters.AddWithValue("@FinalResult", $"total={totalCount.Value}");
            }
            else
            {
                command.Parameters.AddWithValue("@FinalResult", DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}
