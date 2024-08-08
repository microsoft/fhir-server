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

namespace Microsoft.Health.Fhir.Subscriptions.Validation
{
    public static class ChannelInfoValidatorFactory
    {
        public static ISubscriptionChannelValidator Create(SubscriptionChannelType channelType)
        {
            switch (channelType)
            {
                case SubscriptionChannelType.RestHook:
                    return new RestHookChannelValidator();
                default:
                    return null;
            }
        }
    }
}
