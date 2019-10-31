// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Transactions;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface ITransactionHandler : IDisposable
    {
        TransactionScope BeginTransaction();
    }
}
