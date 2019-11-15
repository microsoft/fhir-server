// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlTransactionHandler : ITransactionHandler
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly AsyncLocal<SqlTransactionScope> _sqlTransactionScope = new AsyncLocal<SqlTransactionScope>();

        public SqlTransactionHandler(SqlServerDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        public SqlTransactionScope SqlTransactionScope => _sqlTransactionScope.Value;

        public ITransactionScope BeginTransaction()
        {
            _sqlTransactionScope.Value = new SqlTransactionScope(_configuration);

            return _sqlTransactionScope.Value;
        }

        public void Dispose()
        {
            _sqlTransactionScope.Value?.Dispose();
        }
    }
}
