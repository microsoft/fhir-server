// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold metadata for an individual reindex job.
    /// </summary>
    public class ReindexJobRecord : JobRecord
    {
        public ReindexJobRecord(
            IReadOnlyDictionary<string, string> searchParametersHash,
            ushort maxiumumConcurrency = 1,
            uint maxResourcesPerQuery = 100,
            int queryDelayIntervalInMilliseconds = 500,
            ushort? targetDataStoreUsagePercentage = null)
        {
            EnsureArg.IsNotNull(searchParametersHash, nameof(searchParametersHash));

            // Default values
            SchemaVersion = 1;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
            LastModified = Clock.UtcNow;

            ResourceTypeSearchParameterHashMap = searchParametersHash;
            MaximumConcurrency = maxiumumConcurrency;
            MaximumNumberOfResourcesPerQuery = maxResourcesPerQuery;
            QueryDelayIntervalInMilliseconds = queryDelayIntervalInMilliseconds;
            TargetDataStoreUsagePercentage = targetDataStoreUsagePercentage;
        }

        [JsonConstructor]
        protected ReindexJobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.MaximumConcurrency)]
        public ushort MaximumConcurrency { get; private set; }

        [JsonProperty(JobRecordProperties.Error)]
        public ICollection<OperationOutcomeIssue> Error { get; private set; } = new List<OperationOutcomeIssue>();

        /// <summary>
        /// Use Concurrent dictionary to allow access to specific items in the list
        /// Ignore the byte value field, effective using the dictionary as a hashset
        /// </summary>
        [JsonProperty(JobRecordProperties.QueryList)]
        [JsonConverter(typeof(ReindexJobQueryStatusConverter))]

        public ConcurrentDictionary<ReindexJobQueryStatus, byte> QueryList { get; private set; } = new ConcurrentDictionary<ReindexJobQueryStatus, byte>();

        [JsonProperty(JobRecordProperties.ResourceCounts)]
        public ConcurrentDictionary<string, int> ResourceCounts { get; private set; } = new ConcurrentDictionary<string, int>();

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; set; }

        [JsonProperty(JobRecordProperties.Progress)]
        public int Progress { get; set; }

        [JsonProperty(JobRecordProperties.ResourceTypeSearchParameterHashMap)]
        public IReadOnlyDictionary<string, string> ResourceTypeSearchParameterHashMap { get; private set; }

        [JsonProperty(JobRecordProperties.LastModified)]
        public DateTimeOffset LastModified { get; set; }

        [JsonProperty(JobRecordProperties.FailureCount)]
        public ushort FailureCount { get; set; }

        [JsonProperty(JobRecordProperties.Resources)]
        public ICollection<string> Resources { get; private set; } = new List<string>();

        [JsonProperty(JobRecordProperties.SearchParams)]
        public ICollection<string> SearchParams { get; private set; } = new List<string>();

        [JsonProperty(JobRecordProperties.MaximumNumberOfResourcesPerQuery)]
        public uint MaximumNumberOfResourcesPerQuery { get; private set; }

        /// <summary>
        /// Controls the time between queries of resources to be reindexed
        /// </summary>
        [JsonProperty(JobRecordProperties.QueryDelayIntervalInMilliseconds)]
        public int QueryDelayIntervalInMilliseconds { get; set; }

        /// <summary>
        /// Controls the target percentage of how much of the allocated
        /// data store resources to use
        /// Ex: 1 - 100 percent of provisioned datastore resources
        /// 0 means the value is not set, no throttling will occur
        /// </summary>
        [JsonProperty(JobRecordProperties.TargetDataStoreUsagePercentage)]
        public ushort? TargetDataStoreUsagePercentage { get; set; }

        [JsonIgnore]
        public int PercentComplete
        {
            get
            {
                if (Count > 0 && Progress > 0)
                {
                    return (int)((double)Progress / Count * 100);
                }
                else
                {
                    return 0;
                }
            }
        }

        [JsonIgnore]
        public string ResourceList
        {
            get { return string.Join(",", Resources); }
        }

        [JsonIgnore]
        public string SearchParamList
        {
            get { return string.Join(",", SearchParams); }
        }
    }
}
