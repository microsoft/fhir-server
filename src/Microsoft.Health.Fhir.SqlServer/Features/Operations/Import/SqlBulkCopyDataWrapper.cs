// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public class SqlBulkCopyDataWrapper : IEquatable<SqlBulkCopyDataWrapper>
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

        public bool Equals(SqlBulkCopyDataWrapper other)
        {
            return ResourceSurrogateId.Equals(other.ResourceSurrogateId);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SqlBulkCopyDataWrapper);
        }

        public override int GetHashCode() => ResourceSurrogateId.GetHashCode();
    }
}
