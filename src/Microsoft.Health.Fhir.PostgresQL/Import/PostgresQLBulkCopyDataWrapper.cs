// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL.Import
{
    public class PostgresQLBulkCopyDataWrapper : IEquatable<PostgresQLBulkCopyDataWrapper>
    {
        /// <summary>
        /// FHIR resource metadata for SQL
        /// </summary>
        internal ResourceMetadata? Metadata { get; set; }

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
        public ResourceWrapper? Resource { get; set; }

        /// <summary>
        /// Compressed FHIR raw data
        /// </summary>
#pragma warning disable CA1819
        public byte[]? CompressedRawData { get; set; }
#pragma warning restore CA1819

        /// <summary>
        /// Index for the resource in file
        /// </summary>
        public long Index { get; set; }

        /// <summary>
        /// Import resource for sql operation
        /// </summary>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal BulkImportResourceTypeV1Row BulkImportResource { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public bool Equals(PostgresQLBulkCopyDataWrapper? other)
        {
            return ResourceSurrogateId.Equals(other?.ResourceSurrogateId);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as PostgresQLBulkCopyDataWrapper);
        }

        public override int GetHashCode() => ResourceSurrogateId.GetHashCode();
    }
}
