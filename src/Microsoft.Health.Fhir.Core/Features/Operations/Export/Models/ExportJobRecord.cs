// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class to hold metadata for an individual export request.
    /// </summary>
    public class ExportJobRecord : JobRecord
    {
        public ExportJobRecord(Uri requestUri, string resourceType, string hash, IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = null, PartialDateTime since = null)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(hash, nameof(hash));

            Hash = hash;
            RequestUri = requestUri;
            ResourceType = resourceType;
            RequestorClaims = requestorClaims;
            Since = since;

            // Default values
            SchemaVersion = 1;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
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

        [JsonProperty(JobRecordProperties.Hash)]
        public string Hash { get; private set; }

        [JsonProperty(JobRecordProperties.Output)]
        public IDictionary<string, ExportFileInfo> Output { get; private set; } = new Dictionary<string, ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Error)]
        public IList<ExportFileInfo> Error { get; private set; } = new List<ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Progress)]
        public ExportJobProgress Progress { get; set; }

        [JsonProperty(JobRecordProperties.Since)]
        public PartialDateTime Since { get; private set; }
    }
}
