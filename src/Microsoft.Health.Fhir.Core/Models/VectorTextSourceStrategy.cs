// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Defines how values extracted by a vector SearchParameter are resolved to source text.
    /// </summary>
    public enum VectorTextSourceStrategy
    {
        /// <summary>
        /// Uses each extracted value as source text.
        /// </summary>
        DirectText,

        /// <summary>
        /// Treats extracted values as local Binary resource references and uses their text content.
        /// </summary>
        LocalBinaryReference,
    }
}
