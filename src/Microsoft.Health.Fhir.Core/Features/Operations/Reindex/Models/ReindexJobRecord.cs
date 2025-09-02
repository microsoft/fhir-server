// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold metadata for an individual reindex job.
    /// </summary>
    public class ReindexJobRecord : JobRecord, IJobData
    {
        public const uint MaxMaximumNumberOfResourcesPerQuery = 10000;
        public const uint MinMaximumNumberOfResourcesPerQuery = 1;

        public const uint MaxMaximumNumberOfResourcesPerWrite = 10000;
        public const uint MinMaximumNumberOfResourcesPerWrite = 1;

        public ReindexJobRecord(
            IReadOnlyDictionary<string, string> searchParametersHash,
            IReadOnlyCollection<string> targetResourceTypes,
            IReadOnlyCollection<string> targetSearchParameterTypes,
            IReadOnlyCollection<string> searchParameterResourceTypes,
            uint maxResourcesPerQuery = 100,
            uint maxResourcesPerWrite = 1000,
            int queryDelayIntervalInMilliseconds = 500,
            int typeId = (int)JobType.ReindexOrchestrator,
            ushort? targetDataStoreUsagePercentage = null)
        {
            ResourceTypeSearchParameterHashMap = EnsureArg.IsNotNull(searchParametersHash, nameof(searchParametersHash));
            TargetResourceTypes = EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));
            TargetSearchParameterTypes = EnsureArg.IsNotNull(targetSearchParameterTypes, nameof(targetSearchParameterTypes));
            SearchParameterResourceTypes = EnsureArg.IsNotNull(searchParameterResourceTypes, nameof(searchParameterResourceTypes));
            TypeId = typeId;

            // Default values
            SchemaVersion = 1;
            Id = Guid.NewGuid().ToString();
            Status = OperationStatus.Queued;

            QueuedTime = Clock.UtcNow;
            LastModified = Clock.UtcNow;

            // check for MaximumNumberOfResourcesPerQuery boundary
            if (maxResourcesPerQuery < MinMaximumNumberOfResourcesPerQuery || maxResourcesPerQuery > MaxMaximumNumberOfResourcesPerQuery)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumNumberOfResourcesPerQuery), MinMaximumNumberOfResourcesPerQuery.ToString(), MaxMaximumNumberOfResourcesPerQuery.ToString()));
            }
            else
            {
                MaximumNumberOfResourcesPerQuery = maxResourcesPerQuery;
            }

            // check for MaximumNumberOfResourcesPerWrite boundary
            if (maxResourcesPerWrite < MinMaximumNumberOfResourcesPerWrite || maxResourcesPerWrite > MaxMaximumNumberOfResourcesPerWrite)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumNumberOfResourcesPerWrite), MinMaximumNumberOfResourcesPerWrite.ToString(), MaxMaximumNumberOfResourcesPerWrite.ToString()));
            }
            else
            {
                MaximumNumberOfResourcesPerWrite = maxResourcesPerWrite;
            }

            // check for TargetResourceTypes boundary
            foreach (var type in targetResourceTypes)
            {
                ModelInfoProvider.EnsureValidResourceType(type, nameof(type));
            }
        }

        [JsonConstructor]
        protected ReindexJobRecord()
        {
        }

        /// <summary>
        /// Use Concurrent dictionary to allow access to specific items in the list
        /// Ignore the byte value field, effective using the dictionary as a hashset
        /// </summary>
        [JsonProperty(JobRecordProperties.QueryList)]
        [JsonConverter(typeof(ReindexJobQueryStatusConverter))]

        public ConcurrentDictionary<ReindexJobQueryStatus, byte> QueryList { get; private set; } = new ConcurrentDictionary<ReindexJobQueryStatus, byte>();

        [JsonProperty(JobRecordProperties.ResourceCounts)]
        [JsonConverter(typeof(ReindexJobQueryResourceCountsConverter))]
        public ConcurrentDictionary<string, SearchResultReindex> ResourceCounts { get; private set; } = new ConcurrentDictionary<string, SearchResultReindex>();

        [JsonProperty(JobRecordProperties.Count)]
        public long Count { get; set; }

        [JsonProperty(JobRecordProperties.Progress)]
        public long Progress { get; set; }

        [JsonProperty(JobRecordProperties.ResourceTypeSearchParameterHashMap)]
        public IReadOnlyDictionary<string, string> ResourceTypeSearchParameterHashMap { get; private set; }

        [JsonProperty(JobRecordProperties.LastModified)]
        public DateTimeOffset LastModified { get; set; }

        [JsonProperty(JobRecordProperties.FailureCount)]
        public long FailureCount { get; set; }

        [JsonProperty(JobRecordProperties.Resources)]
        public ICollection<string> Resources { get; private set; } = new List<string>();

        [JsonProperty(JobRecordProperties.SearchParams)]
        public ICollection<string> SearchParams { get; private set; } = new List<string>();

        [JsonProperty(JobRecordProperties.MaximumNumberOfResourcesPerQuery)]
        public uint MaximumNumberOfResourcesPerQuery { get; private set; }

        [JsonProperty(JobRecordProperties.MaximumNumberOfResourcesPerWrite)]
        public uint MaximumNumberOfResourcesPerWrite { get; private set; }

        /// <summary>
        /// A user can optionally limit the scope of the Reindex job to specific
        /// resource types
        /// </summary>
        [JsonProperty(JobRecordProperties.TargetResourceTypes)]
        public IReadOnlyCollection<string> TargetResourceTypes { get; private set; } = new List<string>();

        /// <summary>
        /// A user can supply a list of search params to force a reindex job even though the status is Enabled
        /// </summary>
        [JsonProperty(JobRecordProperties.TargetSearchParameterTypes)]
        public IReadOnlyCollection<string> TargetSearchParameterTypes { get; private set; } = new List<string>();

        /// <summary>
        /// This will be the base resource types from the <see cref="TargetSearchParameterTypes"/>
        /// </summary>
        [JsonProperty(JobRecordProperties.SearchParameterResourceTypes)]
        public IReadOnlyCollection<string> SearchParameterResourceTypes { get; private set; } = new List<string>();

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

        [JsonIgnore]
        public string TargetSearchParameterTypeList
        {
            get { return string.Join(",", TargetSearchParameterTypes); }
        }

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; internal set; }

        [JsonProperty(JobRecordProperties.GroupId)]
        public long GroupId { get; set; }

        internal ReindexJobRecord Clone()
        {
            return (ReindexJobRecord)MemberwiseClone();
        }
    }
}
