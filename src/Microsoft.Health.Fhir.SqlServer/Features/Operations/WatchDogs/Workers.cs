// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class Workers
    {
        private string _connectionString = string.Empty;
        private SqlUtils.SqlService _configService = null;
        private string _targetConnectionString = string.Empty;

        public Workers(string connectionString)
        {
            _connectionString = SqlUtils.SqlService.GetCanonicalConnectionString(connectionString);
            _configService = new SqlUtils.SqlService(_connectionString);
            _targetConnectionString = GetTargetConnectionString();
            if (IsSharded(_targetConnectionString, _configService))
            {
                try
                {
                    _ = new CopyWorker(_targetConnectionString);
                    _ = new IndexRebuildWorker(_targetConnectionString);
                    _ = new QueryWorker(_targetConnectionString);
                }
                catch (Exception e)
                {
                    _configService.LogEvent("Workers", "Error", "Create Workers", text: e.ToString());
                }
            }
            else
            {
                _ = new CopyWorkerNotSharded(_targetConnectionString);
                _ = new IndexRebuildWorkerNotSharded(_targetConnectionString);
            }
        }

        private string GetTargetConnectionString()
        {
            string targetConnectionString = null;
            try
            {
                using var conn = _configService.GetConnection();
                using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'TargetConnectionString'", conn);
                var str = cmd.ExecuteScalar();
                targetConnectionString = str == null ? null : (string)str;
            }
            catch (Exception e)
            {
                _configService.LogEvent("Workers", "Error", "GetTargetConnectionString", text: e.ToString());
            }

            return targetConnectionString;
        }

        public static bool IsSharded(string connectionString, SqlUtils.SqlService loggingService)
        {
            try
            {
                if (connectionString == null)
                {
                    return false;
                }

                // handle case when database does not exist yet
                var builder = new SqlConnectionStringBuilder(connectionString);
                var db = builder.InitialCatalog;
                builder.InitialCatalog = "master";
                using var connDb = new SqlConnection(builder.ToString());
                connDb.Open();
                using var cmdDb = new SqlCommand($"IF EXISTS (SELECT * FROM sys.databases WHERE name = @db) SELECT 1 ELSE SELECT 0", connDb);
                cmdDb.Parameters.AddWithValue("@db", db);
                var dbExists = (int)cmdDb.ExecuteScalar();
                if (dbExists == 1)
                {
                    using var conn = new SqlConnection(connectionString);
                    conn.Open();
                    using var cmd = new SqlCommand("IF object_id('Shards') IS NOT NULL SELECT count(*) FROM dbo.Shards ELSE SELECT 0", conn);
                    var shards = (int)cmd.ExecuteScalar();
                    return shards > 0;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                loggingService.LogEvent("Workers", "Error", "IsSharded", text: e.ToString());

                return false;
            }
        }
    }
}
