// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public class SqlBulkCopyDataWrapper
    {
        /// <summary>
        /// FHIR resource metadata for SQL
        /// </summary>
        internal ResourceMetadata Metadata { get; set; }

        /// <summary>
        /// Resource type id for sql mapping
        /// </summary>
        public short ResourceTypeId { get; set; }

        /// <summary>
        /// Assigned resource surrogate id
        /// </summary>
        public long ResourceSurrogateId { get; set; }

        /// <summary>
        /// Extracted resource wrapper
        /// </summary>
        public ResourceWrapper Resource { get; set; }

        /// <summary>
        /// Compressed FHIR raw data
        /// </summary>
#pragma warning disable CA1819
        public byte[] CompressedRawData { get; set; }
#pragma warning restore CA1819

        /// <summary>
        /// Index for the resource in file
        /// </summary>
        public long Index { get; set; }

        /// <summary>
        /// Import resource for sql operation
        /// </summary>
        internal BulkImportResourceTypeV1Row BulkImportResource { get; set; }
    }
}
