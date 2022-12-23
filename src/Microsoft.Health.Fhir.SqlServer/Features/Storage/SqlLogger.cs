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
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly string _sqlLoggerConnectionString;

        internal SqlLogger(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
        {
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlLoggerConnectionString = GetConnectionStringAsync(CancellationToken.None).Result;
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

        private async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateRetrySqlCommand();

            cmd.CommandText = "SELECT Char FROM dbo.Parameters WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", "SqlLoggerConnectionString");
            var value = await cmd.ExecuteScalarAsync(cancellationToken);
            return value == null ? null : (string)value;
        }
    }
}
