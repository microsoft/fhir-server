// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlConnectionWrapperFactory
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly SqlTransactionHandler _sqlTransactionHandler;

        public SqlConnectionWrapperFactory(SqlServerDataStoreConfiguration configuration, SqlTransactionHandler sqlTransactionHandler)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));

            _configuration = configuration;
            _sqlTransactionHandler = sqlTransactionHandler;
        }

        public SqlConnectionWrapper ObtainSqlConnectionWrapper(bool enlistInTransaction = false)
        {
            return new SqlConnectionWrapper(_configuration, _sqlTransactionHandler, enlistInTransaction);
        }
    }
}
