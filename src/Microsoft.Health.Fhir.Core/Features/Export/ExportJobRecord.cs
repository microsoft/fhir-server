// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobRecord
    {
        public ExportJobRecord(CreateExportRequest exportRequest, int jobSchemaVersion)
        {
            EnsureArg.IsNotNull(exportRequest, nameof(exportRequest));
            EnsureArg.IsGt(jobSchemaVersion, 0, nameof(jobSchemaVersion));

            Request = exportRequest;
            JobSchemaVersion = jobSchemaVersion;

            // Default values
            JobStatus = OperationStatus.Queued;
            Id = Guid.NewGuid().ToString();
            QueuedTime = DateTimeOffset.Now;
            LastModifiedTime = DateTimeOffset.Now;
        }

        [JsonConstructor]
        protected ExportJobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.Request)]
        public CreateExportRequest Request { get; }

        [JsonProperty(JobRecordProperties.Id)]
        public string Id { get; }

        [JsonProperty(JobRecordProperties.JobHash)]
        public string JobHash { get; }

        [JsonProperty(JobRecordProperties.JobQueuedTime)]
        public DateTimeOffset QueuedTime { get; }

        [JsonProperty(JobRecordProperties.PartitonKey)]
        public string PartitionKey { get; } = OperationsConstants.ExportJobPartitionKey;

        [JsonProperty(JobRecordProperties.JobSchemaVersion)]
        public int JobSchemaVersion { get; }

        [JsonProperty(JobRecordProperties.Output)]
        public ExportJobOutput Output { get; } = new ExportJobOutput();

        [JsonProperty(JobRecordProperties.JobStatus)]
        public OperationStatus JobStatus { get; set; }

        [JsonProperty(JobRecordProperties.LastModified)]
        public DateTimeOffset LastModifiedTime { get; set; }

        [JsonProperty(JobRecordProperties.JobStartTime)]
        public DateTimeOffset? JobStartTime { get; set; }

        [JsonProperty(JobRecordProperties.JobEndTime)]
        public DateTimeOffset? JobEndTime { get; set; }

        [JsonProperty(JobRecordProperties.JobCancelledTime)]
        public DateTimeOffset? JobCancelledTime { get; set; }

        [JsonProperty(JobRecordProperties.NumberOfConsecutiveFailures)]
        public int NumberOfConsecutiveFailures { get; set; }

        [JsonProperty(JobRecordProperties.TotalNumberOfFailures)]
        public int TotalNumberOfFailures { get; set; }

        [JsonProperty(JobRecordProperties.Progress)]
        public ExportJobProgress Progress { get; set; }
    }
}
