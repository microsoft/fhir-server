// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    internal class BulkDeleteDescription
    {
        public BulkDeleteDescription(
            bool hardDelete,
            PartialDateTime since,
            PartialDateTime till,
            string type,
            Dictionary<string, string> searchParameters)
        {
            HardDelete = hardDelete;
            Since = since;
            Till = till;
            Type = type;
            SearchParameters = searchParameters;
        }

        [JsonConstructor]
        protected BulkDeleteDescription()
        {
        }

        [JsonProperty(JobRecordProperties.HardDelete)]
        public bool HardDelete { get; private set; }

        [JsonProperty(JobRecordProperties.Since)]
        public PartialDateTime Since { get; private set; }

        [JsonProperty(JobRecordProperties.Till)]
        public PartialDateTime Till { get; private set; }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }

        [JsonProperty(JobRecordProperties.SearchParameters)]
        public Dictionary<string, string> SearchParameters { get; private set; }
    }
}
