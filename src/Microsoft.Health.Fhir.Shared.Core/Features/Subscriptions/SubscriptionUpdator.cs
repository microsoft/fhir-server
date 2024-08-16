// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Subscriptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Subscriptions
{
    public class SubscriptionUpdator : ISubscriptionUpdator
    {
        public ResourceElement UpdateStatus(ResourceElement subscription, string status)
        {
            var poco = subscription.ToPoco<Subscription>();
#if (R4 || Stu3) && !R4B
            poco.Status = status switch
            {
                "active" => Subscription.SubscriptionStatus.Active,
                "requested" => Subscription.SubscriptionStatus.Requested,
                "error" => Subscription.SubscriptionStatus.Error,
                "off" => Subscription.SubscriptionStatus.Off,
                _ => null,
            };

#elif R4B
            poco.Status = status switch
            {
                "active" => SubscriptionStatusCodes.Active,
                "requested" => SubscriptionStatusCodes.Requested,
                "error" => SubscriptionStatusCodes.Error,
                "off" => SubscriptionStatusCodes.Off,
                _ => null,
            };
#endif

            return poco.ToResourceElement();
        }
    }
}
