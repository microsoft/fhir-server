// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public class SubscriptionJobDefinition : IJobData
    {
        public SubscriptionJobDefinition(JobType jobType)
        {
            TypeId = (int)jobType;
        }

        [JsonConstructor]
        protected SubscriptionJobDefinition()
        {
        }

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; set; }

        [JsonProperty("transactionId")]
        public long TransactionId { get; set; }

        [JsonProperty("visibleDate")]
        public DateTime VisibleDate { get; set; }

        [JsonProperty("resourceReferences")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "JSON Poco")]
        public IList<ResourceKey> ResourceReferences { get; set; }

        [JsonProperty("channel")]
        public ChannelInfo Channel { get; set; }
    }
}
