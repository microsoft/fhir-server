// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportDataProcessingTaskResult
    {
        public string ResourceType { get; set; }

        public long CompletedResourceCount { get; set; }

        public long FailedResourceCount { get; set; }

        public string ErrorLogLocation { get; set; }
    }
}
