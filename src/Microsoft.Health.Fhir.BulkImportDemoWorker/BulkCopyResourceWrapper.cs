// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkCopyResourceWrapper
    {
        public BulkCopyResourceWrapper(ResourceElement resource, byte[] rawData, long surrogateId)
        {
            Resource = resource;
            SurrogateId = surrogateId;
            RawData = rawData;
        }

        public ResourceElement Resource { get; set; }

#pragma warning disable CA1819 // Properties should not return arrays
        public byte[] RawData { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public long SurrogateId { get; set; }
    }
}
