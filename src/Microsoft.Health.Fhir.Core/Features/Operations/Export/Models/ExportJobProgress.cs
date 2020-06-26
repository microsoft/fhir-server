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
        public ExportJobProgress(string continuationToken, uint page, string resourceId = null, ExportJobProgress subSearch = null)
        {
            ContinuationToken = continuationToken;
            Page = page;
            TriggeringResourceId = resourceId;
            SubSearch = subSearch;
        }

        [JsonConstructor]
        protected ExportJobProgress()
        {
        }

        [JsonProperty(JobRecordProperties.Query)]
        public string ContinuationToken { get; private set; }

        [JsonProperty(JobRecordProperties.Page)]
        public uint Page { get; private set; }

        [JsonProperty(JobRecordProperties.TriggeringResourceId)]
        public string TriggeringResourceId { get; private set; }

        [JsonProperty(JobRecordProperties.SubSearch)]
        public ExportJobProgress SubSearch { get; private set; }

        public void UpdateContinuationToken(string continuationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(continuationToken, nameof(continuationToken));

            ContinuationToken = continuationToken;
            Page++;
        }

        public void NewSubSearch(string resourceId)
        {
            SubSearch = new ExportJobProgress(null, 0, resourceId);
        }

        public void ClearSubSearch()
        {
            SubSearch = null;
        }
    }
}
