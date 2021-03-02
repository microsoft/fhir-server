// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkImportTaskContext
    {
        public long ProcessedResourceCount { get; set; }

        public long ImportedResourceCount { get; set; }

        public long ImportedSearchParamCount { get; set; }
    }
}
