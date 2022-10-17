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
        public const byte CopyQueueType = 3;

        public bool JobQueueIsNotEmpty(byte queueType = CopyQueueType)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.JobQueue WHERE QueueType = @QueueType", conn) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            var cnt = (int)command.ExecuteScalar();
            return cnt > 0;
        }

        public void DequeueJob(byte queueType, out long groupId, out long jobId, out long version, out string definition, long? inputJobId = null, string connStr = null)
        {
            definition = null;
            groupId = -1L;
            jobId = -1L;
            version = 0;

            using var conn = new SqlConnection(connStr ?? ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueJob", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            command.Parameters.AddWithValue("@HeartbeatTimeoutSec", 600);
            if (inputJobId.HasValue)
            {
                command.Parameters.AddWithValue("@InputJobId", inputJobId.Value);
            }

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

        public void DequeueJob(byte queueType, out long jobId, out long version, out string definition, long? inputJobId = null, string connStr = null)
        {
            DequeueJob(queueType, out var _, out jobId, out version, out definition, inputJobId, connStr);
        }

        public void PutJobHeartbeat(byte queueType, long jobId, long version, string connStr = null)
        {
            using var conn = new SqlConnection(connStr ?? ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobHeartbeat", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            command.Parameters.AddWithValue("@Version", version);
            command.ExecuteNonQuery();
        }

        public void CompleteJob(byte queueType, long jobId, long version, bool failed, int? data = null, string result = null, string connStr = null)
        {
            using var conn = new SqlConnection(connStr ?? ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Failed", failed);
            command.Parameters.AddWithValue("@RequestCancellationOnFailure", true);
            if (data.HasValue)
            {
                command.Parameters.AddWithValue("@Data", data.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@Data", DBNull.Value);
            }

            if (result != null)
            {
                command.Parameters.AddWithValue("@FinalResult", result);
            }
            else
            {
                command.Parameters.AddWithValue("@FinalResult", DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}
