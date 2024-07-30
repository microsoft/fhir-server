// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public interface ISubscriptionManager
    {
        Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync(CancellationToken cancellationToken);

        Task SyncSubscriptionsAsync(CancellationToken cancellationToken);
    }
}
