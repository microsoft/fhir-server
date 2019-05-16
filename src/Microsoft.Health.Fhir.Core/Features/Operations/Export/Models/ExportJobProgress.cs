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
        public ExportJobProgress(string continuationToken, uint page)
        {
            ContinuationToken = continuationToken;
            Page = page;
        }

        [JsonConstructor]
        protected ExportJobProgress()
        {
        }

        [JsonProperty(JobRecordProperties.Query)]
        public string ContinuationToken { get; private set; }

        [JsonProperty(JobRecordProperties.Page)]
        public uint Page { get; private set; }

        public void UpdateContinuationToken(string continuationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(continuationToken, nameof(continuationToken));

            ContinuationToken = continuationToken;
            Page++;
        }
    }
}
