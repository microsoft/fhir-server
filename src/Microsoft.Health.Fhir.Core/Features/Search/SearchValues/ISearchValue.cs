// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a search value.
    /// </summary>
    public interface ISearchValue : IEquatable<ISearchValue>
    {
        /// <summary>
        /// Gets a flag indicating whether the search value is valid as a composite component or not.
        /// </summary>
        bool IsValidAsCompositeComponent { get; }

        /// <summary>
        /// Determines whether this current value is the minimum when compared to
        /// a collection of search values for the same parameter.
        /// </summary>
        bool IsMin { get; set; }

        /// <summary>
        /// Determines whether this current value is the maximum when compared to
        /// a collection of search values for the same paramter.
        /// </summary>
        bool IsMax { get; set; }

        /// <summary>
        /// Compares two ISearchValue objects of the same type and returns an integer that
        /// indicates their relative position in the sort order.
        /// </summary>
        /// <param name="otherValue">The value to compare against.</param>
        /// <returns>
        /// -1 if the current value comes before the given search value.
        /// 0 if both values will occur at the same position.
        /// 1 if the current value comes after the given search value.
        /// </returns>
        int Compare(ISearchValue otherValue);

        /// <summary>
        /// Accepts the visitor.
        /// </summary>
        /// <param name="visitor">The visitor.</param>
        void AcceptVisitor(ISearchValueVisitor visitor);
    }
}
