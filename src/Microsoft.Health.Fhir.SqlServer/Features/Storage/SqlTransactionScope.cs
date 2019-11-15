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
        private readonly SqlTransactionHandler _sqlTransactionHandler;
        private bool _isDisposed;

        public SqlTransactionScope(SqlServerDataStoreConfiguration configuration, SqlTransactionHandler sqlTransactionHandler)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));

            SqlConnection = new SqlConnection(configuration.ConnectionString);
            SqlConnection.Open();

            SqlTransaction = SqlConnection.BeginTransaction();

            _sqlTransactionHandler = sqlTransactionHandler;
        }

        public SqlConnection SqlConnection { get; private set; }

        public SqlTransaction SqlTransaction { get; private set; }

        public void Complete()
        {
            SqlTransaction.Commit();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            SqlConnection?.Dispose();
            SqlTransaction?.Dispose();

            SqlConnection = null;
            SqlTransaction = null;

            _isDisposed = true;

            _sqlTransactionHandler.Dispose();
        }
    }
}
