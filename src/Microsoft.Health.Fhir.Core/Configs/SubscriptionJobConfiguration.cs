// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration for the subscription processing job queue.
    /// </summary>
    public class SubscriptionJobConfiguration : HostingBackgroundServiceQueueItem
    {
        public SubscriptionJobConfiguration()
        {
            Queue = QueueType.Subscriptions;
        }
    }
}
