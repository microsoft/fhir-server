// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;

namespace FhirSchemaManager
{
    public static class SchemaDataStore
    {
        public const string DeleteQuery = "DELETE FROM dbo.SchemaVersion WHERE Version = {0} AND Status = 'failed'";

        public static void ExecuteQuery(string connectionString, string queryString, int version)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandText = queryString;
                command.CommandType = CommandType.Text;

                try
                {
                    command.ExecuteNonQueryAsync();
                }
                catch (SqlException)
                {
                    ExecuteUpsertQuery(connectionString, version, "failed");
                    throw;
                }
            }
        }

        public static void ExecuteUpsertQuery(string connectionString, int version, string status)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                var upsertCommand = new SqlCommand("dbo.UpsertSchemaVersion", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                upsertCommand.Parameters.AddWithValue("@version", version);
                upsertCommand.Parameters.AddWithValue("@status", status);

                connection.Open();
                upsertCommand.ExecuteNonQuery();
            }
        }
    }
}
