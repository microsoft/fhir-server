// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.Sql
{
    public class SqlConnectionBuilder : ISqlConnectionBuilder
    {
        private readonly string _connectionString;

        public SqlConnectionBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string DefaultDatabase { get; }

        public SqlConnection GetSqlConnection(string initialCatalog = null, int? maxPoolSize = null)
        {
            return new SqlConnection(_connectionString);
        }

        public async Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, int? maxPoolSize = null, CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(new SqlConnection(_connectionString));
        }

        public async Task<SqlConnection> GetReadOnlySqlConnectionAsync(string initialCatalog = null, int? maxPoolSize = null, CancellationToken cancellationToken = default)
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            builder.ApplicationIntent = ApplicationIntent.ReadOnly;
            return await Task.FromResult(new SqlConnection(builder.ToString()));
        }
    }
}
