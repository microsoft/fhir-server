// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    // Holds properties specific to Sql search.
    public class SqlSearchOptions : SearchOptions
    {
        public SqlSearchOptions(SearchOptions searchOptions)
            : base(searchOptions)
        {
        }

        /// <summary>
        /// Marks whether we need to execute the second set of queries for (certain types of) sort.
        /// </summary>
        public bool SortQuerySecondPhase { get; internal set; } = false;

        /// <summary>
        /// Sets whether this search query is of type sort with filter.
        /// </summary>
        public bool IsSortWithFilter { get; internal set; } = false;

        /// <summary>
        /// Keeps track of whether we searched for sort values as part of the current SQL query.
        /// </summary>
        public bool? DidWeSearchForSortValue { get; internal set; }

        /// <summary>
        /// Performs a shallow clone of this instance
        /// </summary>
        public SqlSearchOptions CloneSqlSearchOptions() => (SqlSearchOptions)MemberwiseClone();

        internal SqlSearchType GetSearchTypeFromOptions()
        {
            SqlSearchType searchType = 0;

            if (ResourceVersionTypes.HasFlag(ResourceVersionType.Latest))
            {
                searchType |= SqlSearchType.Default;
            }

            if (ResourceVersionTypes.HasFlag(ResourceVersionType.Histoy))
            {
                searchType |= SqlSearchType.IncludeHistory;
            }

            if (ResourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted))
            {
                searchType |= SqlSearchType.IncludeDeleted;
            }

            return searchType;
        }
    }
}
