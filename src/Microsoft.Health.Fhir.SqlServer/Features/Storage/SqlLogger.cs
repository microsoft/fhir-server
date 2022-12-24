// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlLogger
    {
        private readonly string _sqlLoggerConnectionString;

        internal SqlLogger(string sqlLoggerConnectionString)
        {
            _sqlLoggerConnectionString = sqlLoggerConnectionString;
        }

        internal void TryLogEvent(string process, string status, string text)
        {
            if (_sqlLoggerConnectionString == null)
            {
                return;
            }

            try
            {
                using var cmd = new SqlCommand("dbo.LogEvent");
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Process", process);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Text", text);
                using var conn = new SqlConnection(_sqlLoggerConnectionString);
                cmd.Connection = conn;
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }

        internal static async Task<string> GetConnectionStringAsync(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
        {
            using var conn = await sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "SELECT Char FROM dbo.Parameters WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", "SqlLoggerConnectionString");
            var value = await cmd.ExecuteScalarAsync(CancellationToken.None);
            return value == null ? null : (string)value;
        }
    }
}
