// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlTransactionHandler : ITransactionHandler
    {
        public SqlTransactionScope SqlTransactionScope { get; private set; }

        public ITransactionScope BeginTransaction()
        {
            Debug.Assert(SqlTransactionScope == null, "The existing SQL transaction scope should be completed before starting a new transaction.");

            SqlTransactionScope = new SqlTransactionScope(this);

            return SqlTransactionScope;
        }

        public void Dispose()
        {
            SqlTransactionScope?.Dispose();

            SqlTransactionScope = null;
        }
    }
}
