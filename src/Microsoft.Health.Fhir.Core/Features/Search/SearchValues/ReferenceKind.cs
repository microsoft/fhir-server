// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Defines the kinds of reference.
    /// </summary>
    public enum ReferenceKind
    {
        /// <summary>
        /// The reference can be an internal or external.
        /// </summary>
        InternalOrExternal,

        /// <summary>
        /// The reference is an internal reference.
        /// </summary>
        Internal,

        /// <summary>
        /// The reference is an external reference.
        /// </summary>
        External,
    }
}
