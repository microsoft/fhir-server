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
                // "reason": "Alert on Diabetes with Complications Diagnosis",
                // "criteria": "Condition?code=http://hl7.org/fhir/sid/icd-10|E11.6",
                new SubscriptionInfo(
                    null,
                    new ChannelInfo
                    {
                        ChannelType = SubscriptionChannelType.Storage,
                        ContentType = SubscriptionContentType.FullResource,
                        MaxCount = 100,
                        Properties = new Dictionary<string, string>
                        {
                            { "container", "sync-all" },
                        },
                    }),
                new SubscriptionInfo(
                    "Patient",
                    new ChannelInfo
                    {
                        ChannelType = SubscriptionChannelType.Storage,
                        ContentType = SubscriptionContentType.FullResource,
                        MaxCount = 100,
                        Properties = new Dictionary<string, string>
                        {
                            { "container", "sync-patient" },
                        },
                    }),
            };

            return Task.FromResult(list);
        }
    }
}
