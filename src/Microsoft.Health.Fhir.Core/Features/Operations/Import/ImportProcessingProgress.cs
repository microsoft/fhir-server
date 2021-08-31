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
        public long SucceedImportCount { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedImportCount { get; set; }

        /// <summary>
        /// Current index for last checkpoint
        /// </summary>
        public long CurrentIndex { get; set; }

        /// <summary>
        /// Importer initialized status
        /// </summary>
        public bool NeedCleanData { get; set; }
    }
}
