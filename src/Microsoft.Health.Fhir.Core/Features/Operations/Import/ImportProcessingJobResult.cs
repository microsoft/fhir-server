// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingJobResult
    {
        /// <summary>
        /// Succeeded imported resource count
        /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        /// Processed bytes from blob/file
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// If any failure processing resource, error log would be uploaded.
        /// </summary>
        public string ErrorLogLocation { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string ErrorDetails { get; set; }

        public long SucceedCount { get; set; } // TODO: Remove in stage 3

        public long FailedCount { get; set; } // TODO: Remove in stage 3
    }
}
