// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    public interface ISubscriptionChannel
    {
        Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, ChannelInfo channelInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken);
    }
}
