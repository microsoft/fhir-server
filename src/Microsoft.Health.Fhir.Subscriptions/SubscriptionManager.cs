// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions
{
    public class SubscriptionManager
    {
        public Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync()
        {
            throw new NotImplementedException();
        }
    }
}
