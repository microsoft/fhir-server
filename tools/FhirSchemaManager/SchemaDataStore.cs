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
        public const string DeleteQuery = "DELETE FROM dbo.SchemaVersion WHERE Version = @version AND Status = @status";
        public const string Failed = "failed";

        public static void ExecuteScript(string connectionString, string queryString, int version)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                using (var command = new SqlCommand(queryString, connection))
                {
                    try
                    {
                        command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException)
                    {
                        ExecuteUpsert(connectionString, version, Failed);
                        throw;
                    }
                }
            }
        }

        public static void ExecuteDelete(string connectionString, int version, string status)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                using (var deleteCommand = new SqlCommand(DeleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@version", version);
                    deleteCommand.Parameters.AddWithValue("@status", status);

                    deleteCommand.ExecuteNonQuery();
                }
            }
        }

        public static void ExecuteUpsert(string connectionString, int version, string status)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                using (var upsertCommand = new SqlCommand("dbo.UpsertSchemaVersion", connection))
                {
                    upsertCommand.CommandType = CommandType.StoredProcedure;
                    upsertCommand.Parameters.AddWithValue("@version", version);
                    upsertCommand.Parameters.AddWithValue("@status", status);

                    upsertCommand.ExecuteNonQuery();
                }
            }
        }
    }
}
