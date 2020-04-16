// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Abstractions.Features.Transactions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal class CosmosTransactionHandler : ITransactionHandler
    {
        public ITransactionScope BeginTransaction()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}
