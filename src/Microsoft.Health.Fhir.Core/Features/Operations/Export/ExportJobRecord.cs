// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    /// <summary>
    /// Class to hold metadata for an individual export request.
    /// </summary>
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
        public CreateExportRequest Request { get; private set; }

        [JsonProperty(JobRecordProperties.Id)]
        public string Id { get; private set; }

        [JsonProperty(JobRecordProperties.JobHash)]
        public string JobHash { get; private set; }

        [JsonProperty(JobRecordProperties.JobQueuedTime)]
        public DateTimeOffset QueuedTime { get; private set; }

        [JsonProperty(JobRecordProperties.PartitonKey)]
        public string PartitionKey { get; private set; } = OperationsConstants.ExportJobPartitionKey;

        [JsonProperty(JobRecordProperties.JobSchemaVersion)]
        public int JobSchemaVersion { get; private set; }

        [JsonProperty(JobRecordProperties.Output)]
        public List<ExportFileInfo> Output { get; private set; } = new List<ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Error)]
        public List<ExportFileInfo> Errors { get; private set; } = new List<ExportFileInfo>();

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
