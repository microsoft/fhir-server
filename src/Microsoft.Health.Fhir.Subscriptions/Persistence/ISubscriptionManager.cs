﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public interface ISubscriptionManager
    {
        Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync(CancellationToken cancellationToken);
    }
}
