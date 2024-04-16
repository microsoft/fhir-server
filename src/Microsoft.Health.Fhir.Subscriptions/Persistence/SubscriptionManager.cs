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

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public class SubscriptionManager : ISubscriptionManager
    {
        public Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<SubscriptionInfo> list = new List<SubscriptionInfo>
            {
                new SubscriptionInfo(
                    "Resource",
                    new ChannelInfo
                    {
                        ChannelType = SubscriptionChannelType.Storage,
                        ContentType = SubscriptionContentType.FullResource,
                        MaxCount = 100,
                    }),
            };

            return Task.FromResult(list);
        }
    }
}
