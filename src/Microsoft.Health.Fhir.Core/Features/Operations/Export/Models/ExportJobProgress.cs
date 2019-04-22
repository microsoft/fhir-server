// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    public class ExportJobProgress
    {
        public ExportJobProgress(string query, int page)
        {
            EnsureArg.IsNotNullOrWhiteSpace(query, nameof(query));

            Query = query;
            Page = page;
        }

        [JsonConstructor]
        protected ExportJobProgress()
        {
        }

        [JsonProperty(JobRecordProperties.Query)]
        public string Query { get; private set; }

        [JsonProperty(JobRecordProperties.Page)]
        public int Page { get; private set; }
    }
}
