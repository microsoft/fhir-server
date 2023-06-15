// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorJobResult
    {
        /// <summary>
        /// Request Uri for the import opearion
        /// </summary>
        public string Request { get; set; }

        /// <summary>
        /// Resource count succeeded to import
        /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        /// Resource count failed to import
        /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        /// Count of jobs created for all blobs/files
        /// </summary>
        public int CreatedJobs { get; set; }

        /// <summary>
        /// Count of completed jobs
        /// </summary>
        public int CompletedJobs { get; set; }

        /// <summary>
        /// Total size of blobs/files to import
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Processed size of blobs/files
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// Orchestrator job progress.
        /// </summary>
        public ImportOrchestratorJobProgress Progress { get; set; }

        public long SucceedImportCount { get; set; } // TODO: remove in stage 3

        public long FailedImportCount { get; set; } // TODO: remove in stage 3

        public int CreatedJobCount { get; set; } // TODO: remove in stage 3

        public long? TotalSizeInBytes { get; set; } // TODO: remove in stage 3

        /// <summary>
        /// Import mode.
        /// </summary>
        public ImportMode ImportMode { get; set; }
    }
}
