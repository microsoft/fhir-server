// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Defines the types of composite search parameters based on the combination of their component types.
    /// </summary>
    public enum CompositeType
    {
        /// <summary>
        /// Composite of Token and Token parameters.
        /// </summary>
        TokenToken,

        /// <summary>
        /// Composite of Token and Quantity parameters.
        /// </summary>
        TokenQuantity,

        /// <summary>
        /// Composite of Token and String parameters.
        /// </summary>
        TokenString,

        /// <summary>
        /// Composite of Token and Number parameters.
        /// </summary>
        TokenNumber,

        /// <summary>
        /// Composite of Token and Date parameters.
        /// </summary>
        TokenDate,

        /// <summary>
        /// Composite of Token and Reference parameters.
        /// </summary>
        TokenReference,

        /// <summary>
        /// Composite of Token, Number, and Number parameters.
        /// Used for range-based searches like Observation.component-code-value-quantity.
        /// </summary>
        TokenNumberNumber,

        /// <summary>
        /// Unknown or unsupported composite type.
        /// </summary>
        Unknown,
    }
}
