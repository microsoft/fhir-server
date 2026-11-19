// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Describes a native SQL vector column emitted by the released schema model generator.
    /// </summary>
    /// <remarks>
    /// The released generator emits <c>VectorColumn</c> for native vector table columns, but the
    /// released SQL model package does not define that descriptor. Vector values cross the stored
    /// procedure boundary as JSON text and are cast to <c>vector(1536)</c> in SQL, so this type is
    /// intentionally limited to schema metadata and column-name generation.
    /// </remarks>
    internal sealed class VectorColumn : Column
    {
        internal VectorColumn(string name, int dimensions)
            : base(name, SqlDbType.NVarChar, nullable: false, length: -1)
        {
            Dimensions = dimensions;
        }

        internal int Dimensions { get; }
    }
}
