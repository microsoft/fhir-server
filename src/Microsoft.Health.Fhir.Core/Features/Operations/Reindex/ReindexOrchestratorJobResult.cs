// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexOrchestratorJobResult
    {
        /// <summary>
        ///        /// Succeeded imported resource count
        ///               /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        ///        /// Failed processing resource count
        ///               /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        ///        /// Critical error during data processing.
        ///               /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Count of jobs created for all blobs/files
        /// </summary>
        public int CreatedJobs { get; set; }

        /// <summary>
        /// Count of completed jobs
        /// </summary>
        public int CompletedJobs { get; set; }
    }
}
