// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Base class for metadata of a background job.
    /// </summary>
    public class JobRecord
    {
        [JsonConstructor]
        protected JobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.Id)]
        public string Id { get; internal set; }

        [JsonProperty(JobRecordProperties.QueuedTime)]
        public DateTimeOffset QueuedTime { get; internal set; }

        [JsonProperty(JobRecordProperties.SchemaVersion)]
        public int SchemaVersion { get; protected set; }

        [JsonProperty(JobRecordProperties.Status)]
        public OperationStatus Status { get; set; }

        [JsonProperty(JobRecordProperties.StartTime)]
        public DateTimeOffset? StartTime { get; set; }

        [JsonProperty(JobRecordProperties.EndTime)]
        public DateTimeOffset? EndTime { get; set; }

        [JsonProperty(JobRecordProperties.CanceledTime)]
        public DateTimeOffset? CanceledTime { get; set; }

        [JsonProperty(JobRecordProperties.FailureDetails)]
        public JobFailureDetails FailureDetails { get; set; }
    }
}
