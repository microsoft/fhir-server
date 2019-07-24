// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class to hold metadata for an individual export request.
    /// </summary>
    public class ExportJobRecord
    {
        private const string SecretPrefix = "Export-Destination-";

        public ExportJobRecord(Uri requestUri, string resourceType, string hash, IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = null)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(hash, nameof(hash));

            Hash = hash;
            RequestUri = requestUri;
            ResourceType = resourceType;
            RequestorClaims = requestorClaims;

            // Default values
            SchemaVersion = 1;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
            SecretName = SecretPrefix + Id;
        }

        [JsonConstructor]
        protected ExportJobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.RequestUri)]
        public Uri RequestUri { get; private set; }

        [JsonProperty(JobRecordProperties.ResourceType)]
        public string ResourceType { get; private set; }

        [JsonProperty(JobRecordProperties.RequestorClaims)]
        public IReadOnlyCollection<KeyValuePair<string, string>> RequestorClaims { get; private set; }

        [JsonProperty(JobRecordProperties.SecretName)]
        public string SecretName { get; private set; }

        [JsonProperty(JobRecordProperties.Id)]
        public string Id { get; private set; }

        [JsonProperty(JobRecordProperties.Hash)]
        public string Hash { get; private set; }

        [JsonProperty(JobRecordProperties.QueuedTime)]
        public DateTimeOffset QueuedTime { get; private set; }

        [JsonProperty(JobRecordProperties.SchemaVersion)]
        public int SchemaVersion { get; private set; }

        [JsonProperty(JobRecordProperties.Output)]
        public IDictionary<string, ExportFileInfo> Output { get; private set; } = new Dictionary<string, ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Error)]
        public IList<ExportFileInfo> Error { get; private set; } = new List<ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Status)]
        public OperationStatus Status { get; set; }

        [JsonProperty(JobRecordProperties.StartTime)]
        public DateTimeOffset? StartTime { get; set; }

        [JsonProperty(JobRecordProperties.EndTime)]
        public DateTimeOffset? EndTime { get; set; }

        [JsonProperty(JobRecordProperties.CanceledTime)]
        public DateTimeOffset? CanceledTime { get; set; }

        [JsonProperty(JobRecordProperties.Progress)]
        public ExportJobProgress Progress { get; set; }
    }
}
