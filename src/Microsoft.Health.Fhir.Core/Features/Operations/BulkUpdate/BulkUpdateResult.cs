// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    public class BulkUpdateResult
    {
        [JsonConstructor]
        public BulkUpdateResult()
        {
            TotalResources = new Dictionary<string, long>();
            ResourcesUpdated = new Dictionary<string, long>();
            ResourcesIgnored = new Dictionary<string, long>();
            ResourcesUpdateFailed = new Dictionary<string, long>();
            ResourcesPatchFailed = new Dictionary<string, long>();
            Issues = new List<string>();
        }

        [JsonProperty(JobRecordProperties.TotalResources)]
        public IDictionary<string, long> TotalResources { get; }

        [JsonProperty(JobRecordProperties.ResourcesUpdated)]
        public IDictionary<string, long> ResourcesUpdated { get; }

        [JsonProperty(JobRecordProperties.ResourcesIgnored)]
        public IDictionary<string, long> ResourcesIgnored { get; }

        [JsonProperty(JobRecordProperties.ResourcesUpdateFailed)]
        public IDictionary<string, long> ResourcesUpdateFailed { get; }

        [JsonProperty(JobRecordProperties.ResourcesPatchFailed)]
        public IDictionary<string, long> ResourcesPatchFailed { get; }

        [JsonProperty(JobRecordProperties.Issues)]
        public ICollection<string> Issues { get; }
    }
}
