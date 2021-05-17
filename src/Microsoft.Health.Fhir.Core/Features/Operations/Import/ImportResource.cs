// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResource
    {
        public ImportResource(long id, long index, ResourceWrapper resource, byte[] compressedRawData)
        {
            Id = id;
            Resource = resource;
            CompressedRawData = compressedRawData;
            Index = index;
        }

        public ImportResource(ResourceWrapper resource, byte[] compressedRawData)
            : this(0, 0, resource, compressedRawData)
        {
        }

        public ImportResource(long id, long index, string importError)
        {
            Id = id;
            Index = index;
            ImportError = importError;
        }

        /// <summary>
        /// Resource index in the resource file
        /// </summary>
        public long Index { get; set; }

        /// <summary>
        /// Resource sequence id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Resource wrapper from raw content
        /// </summary>
        public ResourceWrapper Resource { get; set; }

        /// <summary>
        /// Compressed fhir data
        /// </summary>
#pragma warning disable CA1819
        public byte[] CompressedRawData { get; set; }
#pragma warning restore CA1819

        /// <summary>
        /// Processing error
        /// </summary>
        public string ImportError { get; set; }
    }
}
