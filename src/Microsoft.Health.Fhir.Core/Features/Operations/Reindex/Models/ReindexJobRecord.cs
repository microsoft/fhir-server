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
        public const int MaxMaximumNumberOfResourcesPerQuery = 10000;
        public const int MinMaximumNumberOfResourcesPerQuery = 1;

        public const int MaxMaximumNumberOfResourcesPerWrite = 10000;
        public const int MinMaximumNumberOfResourcesPerWrite = 1;

        public ReindexJobRecord(
            int maxResourcesPerQuery = MaxMaximumNumberOfResourcesPerQuery,
            int maxResourcesPerWrite = MaxMaximumNumberOfResourcesPerWrite,
            int typeId = (int)JobType.ReindexOrchestrator)
        {
            TypeId = typeId;

            Id = Guid.NewGuid().ToString();

            if (maxResourcesPerQuery < MinMaximumNumberOfResourcesPerQuery || maxResourcesPerQuery > MaxMaximumNumberOfResourcesPerQuery)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumNumberOfResourcesPerQuery), MinMaximumNumberOfResourcesPerQuery.ToString(), MaxMaximumNumberOfResourcesPerQuery.ToString()));
            }
            else
            {
                MaximumNumberOfResourcesPerQuery = (uint)maxResourcesPerQuery;
            }

            if (maxResourcesPerWrite < MinMaximumNumberOfResourcesPerWrite || maxResourcesPerWrite > MaxMaximumNumberOfResourcesPerWrite)
            {
                throw new BadRequestException(string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, nameof(MaximumNumberOfResourcesPerWrite), MinMaximumNumberOfResourcesPerWrite.ToString(), MaxMaximumNumberOfResourcesPerWrite.ToString()));
            }
            else
            {
                MaximumNumberOfResourcesPerWrite = (uint)maxResourcesPerWrite;
            }
        }

        [JsonConstructor]
        protected ReindexJobRecord()
        {
        }

        [JsonProperty(JobRecordProperties.ResourceCounts)]
        public ConcurrentDictionary<string, SearchResultReindex> ResourceCounts { get; private set; } = new ConcurrentDictionary<string, SearchResultReindex>();

        [JsonProperty(JobRecordProperties.Count)]
        public long Count { get; set; }

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

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; internal set; }

        internal ReindexJobRecord Clone()
        {
            return (ReindexJobRecord)MemberwiseClone();
        }
    }
}
