// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public class ChannelInfo
    {
        /// <summary>
        /// Interval to send 'heartbeat' notification
        /// </summary>
        public TimeSpan HeartBeatPeriod { get; set; }

        /// <summary>
        /// Timeout to attempt notification delivery
        /// </summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Maximum number of triggering resources included in notification bundles
        /// </summary>
        public int MaxCount { get; set; }

        public SubscriptionChannelType ChannelType { get; set; }

        public SubscriptionContentType ContentType { get; set; }
    }
}
