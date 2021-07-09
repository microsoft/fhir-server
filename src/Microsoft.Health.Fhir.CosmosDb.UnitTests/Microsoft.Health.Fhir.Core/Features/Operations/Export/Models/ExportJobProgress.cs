// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    public class ExportJobProgress
    {
        public ExportJobProgress(string continuationToken, uint page, string resourceId = null, ExportJobProgress subSearch = null, ExportJobFilter filter = null)
        {
            ContinuationToken = continuationToken;
            Page = page;
            TriggeringResourceId = resourceId;
            SubSearch = subSearch;
            CurrentFilter = filter;

            CompletedFilters = new List<ExportJobFilter>();
        }

        [JsonConstructor]
        protected ExportJobProgress()
        {
        }

        [JsonProperty(JobRecordProperties.Query)]
        public string ContinuationToken { get; private set; }

        [JsonProperty(JobRecordProperties.Page)]
        public uint Page { get; private set; }

        /// <summary>
        /// The filter currently being evaluated.
        /// </summary>
        [JsonProperty(JobRecordProperties.CurrentFilter)]
        public ExportJobFilter CurrentFilter { get; private set; }

        /// <summary>
        /// Indicates if all the filters for the job have been evaluated.
        /// </summary>
        [JsonProperty(JobRecordProperties.FilteredSearchesComplete)]
        public ICollection<ExportJobFilter> CompletedFilters { get; private set; }

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

        public void SetFilter(ExportJobFilter filter)
        {
            if (CurrentFilter != null)
            {
                CompletedFilters.Add(CurrentFilter);
            }

            CurrentFilter = filter;
            Page = 0;
            ContinuationToken = null;
        }

        public void MarkFilterFinished()
        {
            SetFilter(null);
        }

        public void NewSubSearch(string resourceId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            SubSearch = new ExportJobProgress(null, 0, resourceId);
        }

        public void ClearSubSearch()
        {
            SubSearch = null;
        }
    }
}
