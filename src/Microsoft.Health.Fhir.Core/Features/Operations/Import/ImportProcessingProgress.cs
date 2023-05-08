// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingProgress
    {
        /// <summary>
        /// Succeed import resource count
        /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        /// Processed blob/file bytes
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// Current index for last checkpoint
        /// </summary>
        public long CurrentIndex { get; set; }
    }
}
