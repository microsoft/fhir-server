// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportResourceWrapper
    {
        public BulkImportResourceWrapper(long resourceSurrogateId, long endOffsetInResource, ResourceWrapper resource, byte[] compressedRawData)
        {
            ResourceSurrogateId = resourceSurrogateId;
            Resource = resource;
            CompressedRawData = compressedRawData;
            EndOffsetInResource = endOffsetInResource;
        }

        public BulkImportResourceWrapper(ResourceWrapper resource, byte[] compressedRawData)
            : this(0, 0, resource, compressedRawData)
        {
        }

        public long EndOffsetInResource { get; set; }

        public long ResourceSurrogateId { get; set; }

        public ResourceWrapper Resource { get; set; }

#pragma warning disable CA1819
        public byte[] CompressedRawData { get; set; }
#pragma warning restore CA1819
    }
}
