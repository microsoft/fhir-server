// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public class SubscriptionInfo
    {
        public SubscriptionInfo(string filterCriteria, ChannelInfo channel, Uri topic, string resourceId, SubscriptionStatus status)
        {
            FilterCriteria = filterCriteria;
            Channel = EnsureArg.IsNotNull(channel, nameof(channel));
            Topic = topic;
            ResourceId = resourceId;
            Status = status;
        }

        [JsonConstructor]
        protected SubscriptionInfo()
        {
        }

        public string FilterCriteria { get; set; }

        public ChannelInfo Channel { get; set; }

        public Uri Topic { get; set; }

        public string ResourceId { get; set; }

        public SubscriptionStatus Status { get; set; }
    }
}
