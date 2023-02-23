// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search result for a reindex query.
    /// </summary>
    public class SearchResultReindex
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResultReindex"/> class.
        /// </summary>
        /// <param name="totalCount">The total count of all resource types</param>
        /// <param name="startResourceSurrogateId">The starting ResourceSurrogateId for the resource type</param>
        /// <param name="endResourceSurrogateId">The ending ResourceSurrogateId for the resource type</param>
        public SearchResultReindex(string resourceType, int totalCount, long startResourceSurrogateId, long endResourceSurrogateId)
        {
            ResourceType = resourceType;
            TotalCount = totalCount;
            StartResourceSurrogateId = startResourceSurrogateId;
            EndResourceSurrogateId = endResourceSurrogateId;
        }

        /// <summary>
        /// Gets the resource type name
        /// </summary>
        public string ResourceType { get; private set; }

        /// <summary>
        /// Gets total number of documents.
        /// </summary>
        public int TotalCount { get; private set; }

        /// <summary>
        /// Gets the starting resource surrogate Id
        /// </summary>
        public long StartResourceSurrogateId { get; private set; }

        /// <summary>
        /// Gets the ending resource surrogate Id
        /// </summary>
        public long EndResourceSurrogateId { get; private set; }
    }
}
