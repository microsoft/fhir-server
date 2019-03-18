// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobRecord
    {
        public ExportJobRecord(ExportRequest exportRequest)
        {
            EnsureArg.IsNotNull(exportRequest, nameof(exportRequest));

            Request = exportRequest;

            JobStatus = ExportJobStatus.Queued;
            Id = Guid.NewGuid().ToString();

            QueuedTime = DateTimeOffset.Now;
            LastModifiedTime = DateTimeOffset.Now;

            Progress = new ExportJobProgress("query", 1);
            Output = new ExportJobOutput();
        }

        [JsonConstructor]
        protected ExportJobRecord()
        {
        }

        [JsonProperty("id")]
        public string Id { get; }

        public string JobHash { get; }

        public ExportJobStatus JobStatus { get; private set; }

        public DateTimeOffset QueuedTime { get; }

        public DateTimeOffset LastModifiedTime { get; private set; }

        public DateTimeOffset JobStartTime { get; private set; }

        public DateTimeOffset JobEndTime { get; private set; }

        public int NumberOfConsecutiveFailures { get; set; }

        public int TotalNumberOfFailures { get; set; }

        [JsonProperty("partitionKey")]
        public string PartitionKey { get; } = "ExportJob";

        public ExportRequest Request { get; }

        public ExportJobProgress Progress { get; private set; }

        public ExportJobOutput Output { get; }

        public void UpdateJobProgress(ExportJobProgress progress)
        {
            EnsureArg.IsNotNullOrEmpty(progress?.Query, nameof(progress));

            Progress = progress;
        }

        public void UpdateLastModifiedTime(DateTimeOffset modifiedTime)
        {
            LastModifiedTime = modifiedTime;
        }

        public void UpdateJobStartTime(DateTimeOffset startTime)
        {
            JobStartTime = startTime;
        }

        public void UpdateJobEndTime(DateTimeOffset endTime)
        {
            JobEndTime = endTime;
        }

        public void UpdateJobStatus(ExportJobStatus jobStatus)
        {
            JobStatus = jobStatus;
        }
    }
}
