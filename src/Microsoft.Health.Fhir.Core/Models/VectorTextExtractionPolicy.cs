// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Defines how values produced by a vector SearchParameter expression become source passages.
    /// </summary>
    public enum VectorTextExtractionPolicy
    {
        /// <summary>
        /// Uses only the first value produced by the SearchParameter expression.
        /// </summary>
        FirstValue,

        /// <summary>
        /// Concatenates all values produced by the SearchParameter expression.
        /// </summary>
        Concatenate,

        /// <summary>
        /// Produces independent passages for each value produced by the SearchParameter expression.
        /// </summary>
        PerValueRow,
    }
}
