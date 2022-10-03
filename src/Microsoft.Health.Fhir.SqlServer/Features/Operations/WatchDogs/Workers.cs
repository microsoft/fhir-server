// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class Workers
    {
        private string _targetConnectionString = string.Empty;
        private CopyWorker _copyWorker;
        private IndexRebuildWorker _indexRebuildWorker;
        private QueryWorker _queryWorker;

        public Workers(string connectionString)
        {
            var connStr = SqlUtils.SqlService.GetCanonicalConnectionString(connectionString);
            var configService = new SqlUtils.SqlService(connStr);
            _targetConnectionString = GetTargetConnectionString(configService);
            if (IsSharded(_targetConnectionString))
            {
                _copyWorker = new CopyWorker(_targetConnectionString);
                _indexRebuildWorker = new IndexRebuildWorker(_targetConnectionString);
                _queryWorker = new QueryWorker(_targetConnectionString);
            }
        }

        private static string GetTargetConnectionString(SqlUtils.SqlService configService)
        {
            using var conn = configService.GetConnection();
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'TargetConnectionString'", conn);
            var str = cmd.ExecuteScalar();
            return str == null ? null : (string)str;
        }

        public static bool IsSharded(string connectionString)
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
    }
}
