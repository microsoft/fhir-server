// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.Exporter
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
#pragma warning disable CA2000

        public async Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, int? maxPoolSize = null, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return new SqlConnection(_connectionString);
        }
    }
}
