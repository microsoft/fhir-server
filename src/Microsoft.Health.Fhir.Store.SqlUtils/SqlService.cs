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
        private string _connectionString;
        private string _secondaryConnectionString;

        public SqlService(string connectionString, string secondaryConnectionString = null)
        {
            _connectionString = connectionString;
            _secondaryConnectionString = secondaryConnectionString;
        }

        public string ConnectionString => GetTrueConnectionString();

        public string DatabaseName => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

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

        public static string GetCanonicalConnectionString(string connectionString)
        {
            var connStr = connectionString.Replace("Trust Server Certificate=True", string.Empty, StringComparison.OrdinalIgnoreCase);
            return connStr;
        }

        public static string ShowConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return $"server={builder.DataSource};database={builder.InitialCatalog}";
        }

        public string GetTrueConnectionString(bool? useSecondaryStore = null)
        {
            if (!useSecondaryStore.HasValue || _secondaryConnectionString == null)
            {
                return _connectionString;
            }

            return useSecondaryStore.Value ? _secondaryConnectionString : _connectionString;
        }
    }
}
