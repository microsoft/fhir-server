// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// The semantic origin of a value parsed into a <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public enum ReferenceParseContext
    {
        /// <summary>
        /// A <c>Reference.reference</c> value in a resource. A recognized REST target or a bare id must satisfy the resource id policy.
        /// </summary>
        ResourceReference,

        /// <summary>
        /// A reference search parameter value. A recognized REST target or a bare id must satisfy the resource id policy.
        /// </summary>
        QueryReference,

        /// <summary>
        /// An <c>id</c> primitive. The entire value must satisfy the resource id policy.
        /// </summary>
        ResourceIdPrimitive,

        /// <summary>
        /// A canonical or uri value. A recognized REST target with a valid id grammar must satisfy the resource id policy.
        /// Any other value is kept as a complete literal.
        /// </summary>
        CanonicalOrUri,
    }
}
