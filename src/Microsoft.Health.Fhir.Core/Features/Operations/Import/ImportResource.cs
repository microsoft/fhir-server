// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResource
    {
        public ImportResource(long id, long index, ResourceWrapper resource)
        {
            Id = id;
            Index = index;
            Resource = resource;
        }

        public ImportResource(ResourceWrapper resource)
            : this(0, 0, resource)
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
        /// Processing error
        /// </summary>
        public string ImportError { get; set; }

        /// <summary>
        /// Compressed raw resource stream
        /// </summary>
        public Stream CompressedStream { get; set; }
    }
}
