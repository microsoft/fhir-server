// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public partial class SqlService : SqlUtils.SqlService
    {
        public const byte CopyQueueType = 3;

        private Random _rand = new Random();

        private byte _partitionId;
        private object _partitioinLocker = new object();
        private byte _numberOfPartitions;

        public SqlService(string connectionString, string secondaryConnectionString = null, byte? numberOfPartitions = null)
            : base(connectionString, secondaryConnectionString)
        {
            _numberOfPartitions = numberOfPartitions.HasValue ? numberOfPartitions.Value : (byte)16;
            _partitionId = (byte)(_numberOfPartitions * _rand.NextDouble());
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

        internal long GetCorrectedMinResourceSurrogateId(int retries, string tbl, short resourceTypeId, long minSurId, long maxSurId)
        {
            if (retries == 0)
            {
                return minSurId;
            }

            var result = minSurId;
            using (var conn = new SqlConnection(ConnectionString))
            {
                conn.Open();
                using var command = new SqlCommand($"SELECT TOP 1 ResourceSurrogateId FROM dbo.{tbl} WHERE ResourceTypeId = {resourceTypeId} AND ResourceSurrogateId BETWEEN {minSurId} AND {maxSurId} ORDER BY ResourceSurrogateId DESC OPTION (MAXDOP 1)", conn) { CommandTimeout = 600 };
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result = reader.GetInt64(0);
                }
            }

            return result;
        }

        internal bool StoreCopyWorkQueueIsNotEmpty()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.StoreCopyWorkQueue", conn) { CommandTimeout = 120 };
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
            command.Parameters.AddWithValue("@QueueType", CopyQueueType);
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

        public void DequeueJob(out short? resourceTypeId, out long unitId, out long version, out string minSurIdOrUrl, out string maxSurId)
        {
            DequeueJob(out var _, out unitId, out version, out var definition);
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

        internal void PutJobHeatbeat(long unitId, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobHeatbeat", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@JobId", unitId);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@ResourceCount", resourceCount.Value);
            }

            command.ExecuteNonQuery();
        }

        public void CompleteJob(long jobId, bool failed, long version, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", (byte)3);
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

            command.Parameters.AddWithValue("@FinalResult", DBNull.Value);

            command.ExecuteNonQuery();
        }

        internal string GetBcpConnectionString()
        {
            var conn = new SqlConnectionStringBuilder(ConnectionString);
            var security = string.IsNullOrEmpty(conn.UserID) ? "/T" : $"/U{conn.UserID} /P{conn.Password}";
            return $"/S{conn.DataSource} /d{conn.InitialCatalog} {security}";
        }

        internal void RegisterDatabaseLogging()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using (var command = new SqlCommand("INSERT INTO Parameters (Id, Char) SELECT 'BcpOut', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM Parameters WHERE Id = 'BcpOut')", conn) { CommandTimeout = 120 })
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("INSERT INTO Parameters (Id, Char) SELECT 'BcpIn', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM Parameters WHERE Id = 'BcpIn')", conn) { CommandTimeout = 120 })
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("INSERT INTO Parameters (Id, Char) SELECT 'GetData', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM Parameters WHERE Id = 'GetData')", conn) { CommandTimeout = 120 })
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
