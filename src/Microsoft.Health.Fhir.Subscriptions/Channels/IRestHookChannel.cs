// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    public interface IRestHookChannel : ISubscriptionChannel
    {
        Task<bool> PublishHandShakeAsync(SubscriptionInfo subscriptionInfo);

        Task<bool> PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo);

        Task<bool> SendPayload(ChannelInfo chanelInfo, string contents);
    }
}
