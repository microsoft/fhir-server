// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class SearchParamMetadata
    {
        public short SearchParamId { get; set; }

#pragma warning disable CA1056 // Uri properties should not be strings
        public string Uri { get; set; }
#pragma warning restore CA1056 // Uri properties should not be strings

        public string Status { get; set; }

        public DateTime LastUpdated { get; set; }

        public bool IsPartiallySupported { get; set; }
    }
}
