// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkImportTaskInput
    {
        public string BlobLocation { get; set; }

        public long StartSurrogateId { get; set; }
    }
}
