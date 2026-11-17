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
            ResourcesUpdated = new Dictionary<string, long>();
            ResourcesIgnored = new Dictionary<string, long>();
            ResourcesPatchFailed = new Dictionary<string, long>();
            Issues = new List<string>();
            OverlongReferenceIds = new List<OverlongReferenceIdCount>();
        }

        [JsonProperty(JobRecordProperties.ResourcesUpdated)]
        public IDictionary<string, long> ResourcesUpdated { get; }

        [JsonProperty(JobRecordProperties.ResourcesIgnored)]
        public IDictionary<string, long> ResourcesIgnored { get; }

        [JsonProperty(JobRecordProperties.ResourcesPatchFailed)]
        public IDictionary<string, long> ResourcesPatchFailed { get; }

        [JsonProperty(JobRecordProperties.Issues)]
        public ICollection<string> Issues { get; }

        /// <summary>
        /// Gets the updated resources with a reference id longer than the maximum resource id length, by resource type and
        /// search parameter. Callers that update it from several threads must lock it.
        /// </summary>
        [JsonProperty(JobRecordProperties.OverlongReferenceIds)]
        public ICollection<OverlongReferenceIdCount> OverlongReferenceIds { get; }

        /// <summary>
        /// Omits <see cref="OverlongReferenceIds"/> from the serialized result when it's empty.
        /// </summary>
        /// <returns><c>true</c> if there are counts to serialize.</returns>
        public bool ShouldSerializeOverlongReferenceIds() => OverlongReferenceIds?.Count > 0;
    }
}
