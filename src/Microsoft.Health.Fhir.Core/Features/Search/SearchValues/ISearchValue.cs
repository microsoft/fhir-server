// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a search value.
    /// </summary>
    public interface ISearchValue
    {
        /// <summary>
        /// Gets a flag indicating whether the search value is valid as a composite component or not.
        /// </summary>
        bool IsValidAsCompositeComponent { get; }

        /// <summary>
        /// Accepts the visitor.
        /// </summary>
        /// <param name="visitor">The visitor.</param>
        void AcceptVisitor(ISearchValueVisitor visitor);
    }
}
