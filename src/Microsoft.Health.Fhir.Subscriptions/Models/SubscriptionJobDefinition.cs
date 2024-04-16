// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
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

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; set; }

        public long TransactionId { get; set; }

        public DateTime VisibleDate { get; set; }
    }
}
