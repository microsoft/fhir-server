// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingJobResult
    {
        /// <summary>
        /// Input File location
        /// </summary>
        public string ResourceLocation { get; set; }

        /// <summary>
        /// FHIR resource type
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// Succeed imported resource count
        /// </summary>
        public long SucceedCount { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedCount { get; set; }

        /// <summary>
        /// If any failure processing resource, error log would be uploaded.
        /// </summary>
        public string ErrorLogLocation { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string ImportError { get; set; }

        /// <summary>
        /// Current index for last checkpoint
        /// </summary>
        public long CurrentIndex { get; set; }
    }
}
