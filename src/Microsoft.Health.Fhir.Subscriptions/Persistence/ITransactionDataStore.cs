// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public interface ITransactionDataStore
    {
        Task<IReadOnlyList<ResourceWrapper>> GetResourcesByTransactionIdAsync(long transactionId, CancellationToken cancellationToken);
    }
}
