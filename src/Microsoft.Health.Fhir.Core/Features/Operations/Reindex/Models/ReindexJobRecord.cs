﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold metadata for an individual reindex job.
    /// </summary>
    public class ReindexJobRecord : JobRecord
    {
        private const ushort MaxMaximumConcurrency = 10;
        private const ushort MinMaximumConcurrency = 1;
        private const uint MaxMaximumNumberOfResourcesPerQuery = 5000;
        private const uint MinMaximumNumberOfResourcesPerQuery = 1;
        private const int MaxQueryDelayIntervalInMilliseconds = 500000;
        private const int MinQueryDelayIntervalInMilliseconds = 5;
        private const ushort MaxTargetDataStoreUsagePercentage = 100;
        private const ushort MinTargetDataStoreUsagePercentage = 0;

        public ReindexJobRecord(
            IReadOnlyDictionary<string, string> searchParametersHash,
            IReadOnlyCollection<string> targetResourceTypes,
            ushort maxiumumConcurrency = 1,
            uint maxResourcesPerQuery = 100,
            int queryDelayIntervalInMilliseconds = 500,
            ushort? targetDataStoreUsagePercentage = null)
        {
            EnsureArg.IsNotNull(searchParametersHash, nameof(searchParametersHash));
            EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));

            // Default values
            SchemaVersion = 1;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
            LastModified = Clock.UtcNow;

            ResourceTypeSearchParameterHashMap = searchParametersHash;

            // check for MaximumConcurrency boundary
            if (maxiumumConcurrency > MaxMaximumConcurrency)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumConcurrency), MinMaximumConcurrency.ToString(), MaxMaximumConcurrency.ToString()));
            }
            else
            {
                MaximumConcurrency = maxiumumConcurrency;
            }

            // check for MaximumNumberOfResourcesPerQuery boundary
            if (maxResourcesPerQuery < MinMaximumNumberOfResourcesPerQuery || maxResourcesPerQuery > MaxMaximumNumberOfResourcesPerQuery)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumNumberOfResourcesPerQuery), MinMaximumNumberOfResourcesPerQuery.ToString(), MaxMaximumNumberOfResourcesPerQuery.ToString()));
            }
            else
            {
                MaximumNumberOfResourcesPerQuery = maxResourcesPerQuery;
            }

            // check for QueryDelayIntervalInMilliseconds boundary
            if (queryDelayIntervalInMilliseconds < MinQueryDelayIntervalInMilliseconds || queryDelayIntervalInMilliseconds > MaxQueryDelayIntervalInMilliseconds)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(QueryDelayIntervalInMilliseconds), MinQueryDelayIntervalInMilliseconds.ToString(), MaxQueryDelayIntervalInMilliseconds.ToString()));
            }
            else
            {
                QueryDelayIntervalInMilliseconds = queryDelayIntervalInMilliseconds;
            }

            // check for TargetDataStoreUsagePercentage boundary
            if (targetDataStoreUsagePercentage < MinTargetDataStoreUsagePercentage || targetDataStoreUsagePercentage > MaxTargetDataStoreUsagePercentage)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(TargetDataStoreUsagePercentage), MinTargetDataStoreUsagePercentage.ToString(), MaxTargetDataStoreUsagePercentage.ToString()));
            }
            else
            {
                TargetDataStoreUsagePercentage = targetDataStoreUsagePercentage;
            }

            // check for TargetResourceTypes boundary
            foreach (var type in targetResourceTypes)
            {
                ModelInfoProvider.EnsureValidResourceType(type, nameof(type));
            }

            TargetResourceTypes = targetResourceTypes;
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

        /// <summary>
        /// A user can optionally limit the scope of the Reindex job to specific
        /// resource types
        /// </summary>
        [JsonProperty(JobRecordProperties.TargetResourceTypes)]
        public IReadOnlyCollection<string> TargetResourceTypes { get; private set; } = new List<string>();

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

        [JsonIgnore]
        public string TargetResourceTypeList
        {
            get { return string.Join(",", TargetResourceTypes); }
        }
    }
}
