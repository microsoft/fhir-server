// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Copy
{
    internal class SqlService : SqlUtils.SqlService
    {
        internal SqlService(string connectionString)
            : base(connectionString)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
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

        internal void DequeueStoreCopyWorkQueue(byte thread, out short? resourceTypeId, out int unitId, out long minSurId, out long maxSurId)
        {
            resourceTypeId = null;
            unitId = 0;
            minSurId = 0;
            maxSurId = 0;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueStoreCopyWorkUnit", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@Thread", thread);
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                resourceTypeId = reader.GetInt16(0);
                unitId = reader.GetInt32(1);
                minSurId = reader.GetInt64(2);
                maxSurId = reader.GetInt64(3);
            }
        }

        internal void CompleteStoreCopyWorkUnit(short resourceTypeId, int unitId, bool failed)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutStoreCopyWorkUnitStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
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
        }
    }
}
