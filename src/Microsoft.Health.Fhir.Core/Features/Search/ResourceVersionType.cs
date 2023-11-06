// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Defines the order of a sort.
    /// </summary>
    [Flags]
    public enum ResourceVersionType
    {
        /// <summary>
        /// Latest version of the resource.
        /// </summary>
        Latest = 1,

        /// <summary>
        /// Previous versions of the resource - i.e. historical.
        /// </summary>
        Histoy = 1 << 1,

        /// <summary>
        /// Resources that have been deleted but are still in the system (soft-delete).
        /// </summary>
        SoftDeleted = 1 << 2,
    }
}
