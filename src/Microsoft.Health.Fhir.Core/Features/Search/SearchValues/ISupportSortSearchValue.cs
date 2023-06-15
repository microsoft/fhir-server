// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Companion interface to <see cref="ISearchValue"/>. Represents properties of a search
    /// value that determine its sort order amongst other search values.
    /// </summary>
    public interface ISupportSortSearchValue : IRangedComparable
    {
        /// <summary>
        /// Determines whether this current value is the minimum when compared to
        /// a collection of search values for the same parameter.
        /// </summary>
        bool IsMin { get; set; }

        /// <summary>
        /// Determines whether this current value is the maximum when compared to
        /// a collection of search values for the same parameter.
        /// </summary>
        bool IsMax { get; set; }
    }
}
