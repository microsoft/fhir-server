// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlBulkCopyDataWrapper
    {
        internal ResourceMetadata Metadata { get; set; }

        internal short ResourceTypeId { get; set; }

        internal long ResourceSurrogateId { get; set; }

        internal ResourceWrapper Resource { get; set; }

#pragma warning disable CA1819
        internal byte[] CompressedRawData { get; set; }
#pragma warning restore CA1819
    }
}
