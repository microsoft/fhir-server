// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlConnectionWrapper : IDisposable
    {
        private readonly bool _enlistInTransactionIfPresent;
        private readonly SqlTransactionHandler _sqlTransactionHandler;

        public SqlConnectionWrapper(SqlServerDataStoreConfiguration configuration, SqlTransactionHandler sqlTransactionHandler, bool enlistInTransactionIfPresent)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));

            _sqlTransactionHandler = sqlTransactionHandler;
            _enlistInTransactionIfPresent = enlistInTransactionIfPresent;

            if (_enlistInTransactionIfPresent && sqlTransactionHandler.SqlTransactionScope?.SqlConnection != null)
            {
                SqlConnection = sqlTransactionHandler.SqlTransactionScope.SqlConnection;
            }
            else
            {
                SqlConnection = new SqlConnection(configuration.ConnectionString);
            }

            if (_enlistInTransactionIfPresent && sqlTransactionHandler.SqlTransactionScope != null && sqlTransactionHandler.SqlTransactionScope.SqlConnection == null)
            {
                sqlTransactionHandler.SqlTransactionScope.SqlConnection = SqlConnection;
            }

            if (SqlConnection.State != ConnectionState.Open)
            {
                SqlConnection.Open();
            }

            if (enlistInTransactionIfPresent && sqlTransactionHandler.SqlTransactionScope != null)
            {
                SqlTransaction = sqlTransactionHandler.SqlTransactionScope.SqlTransaction ?? SqlConnection.BeginTransaction();

                if (sqlTransactionHandler.SqlTransactionScope.SqlTransaction == null)
                {
                    sqlTransactionHandler.SqlTransactionScope.SqlTransaction = SqlTransaction;
                }
            }
        }

        public SqlConnection SqlConnection { get; }

        public SqlTransaction SqlTransaction { get; }

        public SqlCommand CreateSqlCommand()
        {
            SqlCommand sqlCommand = SqlConnection.CreateCommand();
            sqlCommand.Transaction = SqlTransaction;

            return sqlCommand;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_enlistInTransactionIfPresent || _sqlTransactionHandler.SqlTransactionScope == null)
                {
                    SqlConnection?.Dispose();
                    SqlTransaction?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
