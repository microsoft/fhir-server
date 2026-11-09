// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobResult
    {
        /// <summary>
        /// Succeeded reindexed resource count
        /// </summary>
        public long SucceededResourceCount { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Gets or sets whether the processing failure was caused by customer configuration.
        /// </summary>
        [JsonProperty(JobRecordProperties.IsCustomerCaused, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsCustomerCaused { get; set; }
    }
}
