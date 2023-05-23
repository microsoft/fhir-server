// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    internal class BulkDeleteDefinition : IJobData
    {
        public BulkDeleteDefinition(
            JobType jobType,
            DeleteOperation deleteOperation,
            string type,
            IList<Tuple<string, string>> searchParameters,
            string url,
            string baseUrl)
        {
            TypeId = (int)jobType;
            DeleteOperation = deleteOperation;
            Type = type;
            SearchParameters = searchParameters;
            Url = url;
            BaseUrl = baseUrl;
        }

        [JsonConstructor]
        protected BulkDeleteDefinition()
        {
        }

        [JsonProperty(JobRecordProperties.TypeId)]
        public int TypeId { get; set; }

        [JsonProperty(JobRecordProperties.DeleteOperation)]
        public DeleteOperation DeleteOperation { get; private set; }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }

        [JsonProperty(JobRecordProperties.SearchParameters)]
        public IList<Tuple<string, string>> SearchParameters { get; private set; }

        [JsonProperty(JobRecordProperties.Url)]
        public string Url { get; private set; }

        [JsonProperty(JobRecordProperties.BaseUrl)]
        public string BaseUrl { get; private set; }
    }
}
