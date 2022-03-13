// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.SqlUtils
{
    public class SqlService
    {
        public SqlService(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public string DatabaseName => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

        public string ConnectionString { get; }

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
    }
}
