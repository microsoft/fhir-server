// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Copy
{
    internal partial class SqlService : SqlUtils.SqlService
    {
        private Random _rand = new Random();

        private byte _partitionId;
        private object _partitioinLocker = new object();

        internal SqlService(string connectionString)
            : base(connectionString)
        {
            _partitionId = (byte)(16 * _rand.NextDouble());
        }

        private byte GetNextPartitionId()
        {
            lock (_partitioinLocker)
            {
                _partitionId = _partitionId == 15 ? (byte)0 : ++_partitionId;
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

        internal void DequeueStoreCopyWorkQueue(out short? resourceTypeId, out int unitId, out string minSurId, out string maxSurId)
        {
            resourceTypeId = null;
            unitId = 0;
            minSurId = string.Empty;
            maxSurId = string.Empty;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueStoreCopyWorkUnit", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@StartPartitionId", GetNextPartitionId());
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                resourceTypeId = reader.GetInt16(0);
                unitId = reader.GetInt32(1);
                minSurId = reader.GetString(2);
                maxSurId = reader.GetString(3);
            }
        }

        internal void CompleteStoreCopyWorkUnit(int unitId, bool failed)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutStoreCopyWorkUnitStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@UnitId", unitId);
            command.Parameters.AddWithValue("@Failed", failed);
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
