// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Transactions;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class DefaultTransactionScope : ITransactionScope
    {
        private TransactionScope _transactionScope;

        public DefaultTransactionScope()
        {
            _transactionScope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        }

        public void Complete()
        {
            _transactionScope.Complete();
        }

        public void Dispose()
        {
            _transactionScope?.Dispose();
        }
    }
}
