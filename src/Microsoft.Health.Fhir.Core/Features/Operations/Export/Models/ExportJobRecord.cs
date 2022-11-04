// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class to hold metadata for an individual export request.
    /// </summary>
    public class ExportJobRecord : JobRecord, IJobData
    {
        public ExportJobRecord(
            Uri requestUri,
            ExportJobType exportType,
            string exportFormat,
            string resourceType,
            IList<ExportJobFilter> filters,
            string hash,
            uint rollingFileSizeInMB,
            IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = null,
            PartialDateTime since = null,
            PartialDateTime till = null,
            string groupId = null,
            string storageAccountConnectionHash = null,
            string storageAccountUri = null,
            string anonymizationConfigurationCollectionReference = null,
            string anonymizationConfigurationLocation = null,
            string anonymizationConfigurationFileETag = null,
            uint maximumNumberOfResourcesPerQuery = 100,
            uint numberOfPagesPerCommit = 10,
            string storageAccountContainerName = null,
            int parallel = 0,
            int schemaVersion = 2,
            int typeId = (int)JobType.ExportOrchestrator,
            bool smartRequest = false)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(hash, nameof(hash));
            EnsureArg.IsNotNullOrWhiteSpace(exportFormat, nameof(exportFormat));
            EnsureArg.IsGt(schemaVersion, 0, nameof(schemaVersion));

            Hash = hash;
            RequestUri = requestUri;
            ExportType = exportType;
            ExportFormat = exportFormat;
            ResourceType = resourceType;
            Filters = filters;
            RequestorClaims = requestorClaims;
            Since = since;
            GroupId = groupId;
            StorageAccountConnectionHash = storageAccountConnectionHash;
            StorageAccountUri = storageAccountUri;
            MaximumNumberOfResourcesPerQuery = maximumNumberOfResourcesPerQuery;
            NumberOfPagesPerCommit = numberOfPagesPerCommit;
            RollingFileSizeInMB = rollingFileSizeInMB;
            RestartCount = 0;
            TypeId = typeId;
            Parallel = parallel;

            AnonymizationConfigurationCollectionReference = anonymizationConfigurationCollectionReference;
            AnonymizationConfigurationLocation = anonymizationConfigurationLocation;
            AnonymizationConfigurationFileETag = anonymizationConfigurationFileETag;

            // Default values
            SchemaVersion = schemaVersion;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
            Till = till ?? new PartialDateTime(Clock.UtcNow);

            SmartRequest = smartRequest;

            if (string.IsNullOrWhiteSpace(storageAccountContainerName))
            {
                StorageAccountContainerName = Id;
            }
            else
            {
                StorageAccountContainerName = storageAccountContainerName;
            }
        }

        [JsonConstructor]
        protected ExportJobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; internal set; }

        [JsonProperty(JobRecordProperties.RequestUri)]
        public Uri RequestUri { get; internal set; }

        [JsonProperty(JobRecordProperties.ExportType)]
        public ExportJobType ExportType { get; private set; }

        [JsonProperty(JobRecordProperties.ExportFormat)]
        public string ExportFormat { get; private set; }

        [JsonProperty(JobRecordProperties.ResourceType)]
        public string ResourceType { get; private set; }

        /// <summary>
        /// All the filters for specific types included in the job. Set by the _typeFilter parameter.
        /// </summary>
        [JsonProperty(JobRecordProperties.Filters)]
        public IList<ExportJobFilter> Filters { get; private set; }

        [JsonProperty(JobRecordProperties.RequestorClaims)]
        public IReadOnlyCollection<KeyValuePair<string, string>> RequestorClaims { get; private set; }

        [JsonProperty(JobRecordProperties.Hash)]
        public string Hash { get; internal set; }

        [JsonProperty(JobRecordProperties.Output, ItemConverterType = typeof(ExportJobRecordOutputConverter))]
        public IDictionary<string, List<ExportFileInfo>> Output { get; private set; } = new Dictionary<string, List<ExportFileInfo>>();

        [JsonProperty(JobRecordProperties.Error)]
        public IList<ExportFileInfo> Error { get; private set; } = new List<ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Issues)]
        public IList<OperationOutcomeIssue> Issues { get; private set; } = new List<OperationOutcomeIssue>();

        [JsonProperty(JobRecordProperties.Progress)]
        public ExportJobProgress Progress { get; set; }

        [JsonProperty(JobRecordProperties.Since)]
        public PartialDateTime Since { get; private set; }

        [JsonProperty(JobRecordProperties.Till)]
        public PartialDateTime Till { get; private set; }

        [JsonProperty(JobRecordProperties.GroupId)]
        public string GroupId { get; private set; }

        [JsonProperty(JobRecordProperties.StorageAccountConnectionHash)]
        public string StorageAccountConnectionHash { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA1056:Uri properties should not be strings",
            Justification = "Set from an ExportJobConfiguration where the value is a string and is never used as a URI.")]
        [JsonProperty(JobRecordProperties.StorageAccountUri)]
        public string StorageAccountUri { get; private set; }

        [JsonProperty(JobRecordProperties.MaximumNumberOfResourcesPerQuery)]
        public uint MaximumNumberOfResourcesPerQuery { get; private set; }

        [JsonProperty(JobRecordProperties.NumberOfPagesPerCommit)]
        public uint NumberOfPagesPerCommit { get; private set; }

        [JsonProperty(JobRecordProperties.StorageAccountContainerName)]
        public string StorageAccountContainerName { get; private set; }

        [JsonProperty(JobRecordProperties.AnonymizationConfigurationLocation)]
        public string AnonymizationConfigurationLocation { get; private set; }

        [JsonProperty(JobRecordProperties.AnonymizationConfigurationCollectionReference)]
        public string AnonymizationConfigurationCollectionReference { get; private set; }

        [JsonProperty(JobRecordProperties.AnonymizationConfigurationFileETag)]
        public string AnonymizationConfigurationFileETag { get; private set; }

        [JsonProperty(JobRecordProperties.RollingFileSizeInMB)]
        public uint RollingFileSizeInMB { get; private set; }

        [JsonProperty(JobRecordProperties.RestartCount)]
        public uint RestartCount { get; set; }

        [JsonProperty(JobRecordProperties.Parallel)]
        public int Parallel { get; private set; }

        [JsonProperty(JobRecordProperties.SmartRequest)]
        public bool SmartRequest { get; private set; }

        internal ExportJobRecord Clone()
        {
            return (ExportJobRecord)MemberwiseClone();
        }
    }
}
