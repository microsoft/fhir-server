// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    internal class BulkUpdateDefinition : IJobData
    {
        public BulkUpdateDefinition(
            JobType jobType,
            string type,
            IList<Tuple<string, string>> searchParameters,
            string url,
            string baseUrl,
            string parentRequestId,
            string parameters,
            bool isParallel = true,
            bool readNextPage = true,
            string startSurrogateId = null,
            string endSurrogateId = null,
            string globalStartSurrogateId = null,
            string globalEndSurrogateId = null,
            uint maximumNumberOfResourcesPerQuery = 10000)
        {
            TypeId = (int)jobType;
            Type = type;
            SearchParameters = searchParameters;
            Url = url;
            BaseUrl = baseUrl;
            ParentRequestId = parentRequestId;
            Parameters = parameters;
            IsParallel = isParallel;
            ReadNextPage = readNextPage;
            StartSurrogateId = startSurrogateId;
            EndSurrogateId = endSurrogateId;
            GlobalStartSurrogateId = globalStartSurrogateId;
            GlobalEndSurrogateId = globalEndSurrogateId;
            MaximumNumberOfResourcesPerQuery = maximumNumberOfResourcesPerQuery > 0 ? maximumNumberOfResourcesPerQuery : 10000;
        }

        [JsonConstructor]
        protected BulkUpdateDefinition()
        {
        }

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; set; }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }

        [JsonProperty(JobRecordProperties.SearchParameters)]
        public IList<Tuple<string, string>> SearchParameters { get; private set; }

        [JsonProperty(JobRecordProperties.Url)]
        public string Url { get; private set; }

        [JsonProperty(JobRecordProperties.BaseUrl)]
        public string BaseUrl { get; private set; }

        [JsonProperty(JobRecordProperties.ParentRequestId)]
        public string ParentRequestId { get; private set; }

        [JsonProperty(JobRecordProperties.Parameters)]
        public string Parameters { get; set; }

        [JsonProperty(JobRecordProperties.IsParallel)]
        public bool IsParallel { get; private set; }

        [JsonProperty(JobRecordProperties.ReadNextPage)]
        public bool ReadNextPage { get; private set; }

        [JsonProperty(JobRecordProperties.StartSurrogateId)]
        public string StartSurrogateId { get; private set; }

        [JsonProperty(JobRecordProperties.EndSurrogateId)]
        public string EndSurrogateId { get; private set; }

        [JsonProperty(JobRecordProperties.GlobalEndSurrogateId)]
        public string GlobalEndSurrogateId { get; private set; }

        [JsonProperty(JobRecordProperties.GlobalStartSurrogateId)]
        public string GlobalStartSurrogateId { get; private set; }

        [JsonProperty(JobRecordProperties.MaximumNumberOfResourcesPerQuery)]
        public uint MaximumNumberOfResourcesPerQuery { get; private set; }
    }
}
