// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold metadata for one query of a reindex job
    /// </summary>
    public class ReindexJobQueryStatus
    {
        public ReindexJobQueryStatus(string resourceType, string continuationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            ResourceType = resourceType;
            ContinuationToken = continuationToken;
        }

        [JsonConstructor]
        protected ReindexJobQueryStatus()
        {
        }

        [JsonProperty(JobRecordProperties.ContinuationToken)]
        public string ContinuationToken { get; set; }

        /// <summary>
        /// The point at which to start a query
        /// </summary>
        [JsonProperty(JobRecordProperties.StartSurrogateId)]
        public long StartResourceSurrogateId { get; set; }

        [JsonProperty(JobRecordProperties.Status)]
        public OperationStatus Status { get; set; }

        [JsonProperty(JobRecordProperties.LastModified)]
        public DateTimeOffset LastModified { get; set; }

        [JsonProperty(JobRecordProperties.Error)]
        public string Error { get; set; }

        [JsonProperty(JobRecordProperties.FailureCount)]
        public ushort FailureCount { get; set; }

        [JsonProperty(JobRecordProperties.ResourceType)]
        public string ResourceType { get; private set; }
    }
}
