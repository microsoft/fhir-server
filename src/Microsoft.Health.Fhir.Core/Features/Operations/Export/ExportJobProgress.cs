// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobProgress
    {
        public ExportJobProgress(string query, int page)
        {
            EnsureArg.IsNotNullOrEmpty(query, nameof(query));

            Query = query;
            Page = page;
        }

        [JsonConstructor]
        public ExportJobProgress()
        {
        }

        [JsonProperty(JobRecordProperties.Query)]
        public string Query { get; }

        [JsonProperty(JobRecordProperties.Page)]
        public int Page { get; }
    }
}
