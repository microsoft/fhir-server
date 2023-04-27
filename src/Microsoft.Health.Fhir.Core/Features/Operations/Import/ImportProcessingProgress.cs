// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingProgress
    {
        /// <summary>
        /// Succeeded resource count
        /// </summary>
        public long SucceedImportCount { get; set; }

        /// <summary>
        /// Failed resource count
        /// </summary>
        public long FailedImportCount { get; set; }
    }
}
