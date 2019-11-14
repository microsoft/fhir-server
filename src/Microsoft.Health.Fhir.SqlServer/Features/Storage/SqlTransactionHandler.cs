// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlTransactionHandler : ITransactionHandler
    {
        private SqlServerDataStoreConfiguration _configuration;

        public SqlTransactionHandler(SqlServerDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        public SqlTransactionScope SqlTransactionScope { get; private set; }

        public ITransactionScope BeginTransaction()
        {
            SqlTransactionScope = new SqlTransactionScope(_configuration);

            return SqlTransactionScope;
        }

        public void Dispose()
        {
            SqlTransactionScope?.Dispose();
        }
    }
}
