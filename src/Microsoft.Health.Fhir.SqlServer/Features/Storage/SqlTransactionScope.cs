// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlTransactionScope : ITransactionScope
    {
        public SqlTransactionScope(SqlServerDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            SqlConnection = new SqlConnection(configuration.ConnectionString);

            SqlConnection.Open();

            SqlTransaction = SqlConnection.BeginTransaction();
        }

        public SqlConnection SqlConnection { get; }

        public SqlTransaction SqlTransaction { get; }

        public void Complete()
        {
            SqlTransaction.Commit();
        }

        public void Dispose()
        {
            SqlConnection?.Dispose();
            SqlTransaction?.Dispose();
        }
    }
}
