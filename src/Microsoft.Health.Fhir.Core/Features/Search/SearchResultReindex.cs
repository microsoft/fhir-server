// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search result.
    /// </summary>
    public class SearchResultReindex
    {
        public SearchResultReindex()
        {
        }

        public SearchResultReindex(long count)
        {
            Count = count;
        }

        /// <summary>
        /// The count of items to reindex - only has value for cosmos
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// The count of items that have been reindexed
        /// </summary>
        public long CountReindexed { get; set; }

        /// <summary>
        /// Used as a pointer to the Start surrogateId param when searching resources by a range of Start/End resource surrogateIds
        /// </summary>
        public long CurrentResourceSurrogateId { get; set; }

        /// <summary>
        /// The Min resource surrogate id for the resource
        /// </summary>
        public long StartResourceSurrogateId { get; set; }

        /// <summary>
        /// The Max resource surrogate id for the resource
        /// </summary>
        public long EndResourceSurrogateId { get; set; }
    }
}
