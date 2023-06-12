// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobResult
    {
        /// <summary>
        /// Succeeded reindexed resource count
        /// </summary>
        public long SucceededResourceCount { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedResourceCount { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string Error { get; set; }
    }
}
